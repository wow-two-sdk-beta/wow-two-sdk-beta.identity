using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Identity.Tests.E2E.Bff;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2TokenExchange;
using WoW.Two.Sdk.Beta.Be.Identity.Authorization;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.CurrentPrincipal;
using WoW.Two.Sdk.Beta.Be.Identity.CurrentUser;
using WoW.Two.Sdk.Beta.Be.Identity.Jwt;
using WoW.Two.Sdk.Beta.Be.Identity.Signals;
using WoW.Two.Sdk.Beta.Be.Jobs.Core.PersonOperations;
using WoW.Two.Sdk.Beta.Be.Messaging.Webhooks;

namespace Identity.Tests.E2E.Oidc;

/// <summary>
/// The revocation signal end to end: the real service, a real resource server verifying offline and hearing signals, a BFF and
/// long operations. A revoked token is refused on the deny-list path, a role change gives fresh claims with no sign-in, a lost
/// signal is bounded by the token's lifetime, and a job continues on sign-out and cancels on access loss.
/// </summary>
public sealed class RevocationSignalTests : IAsyncLifetime
{
    private const string Password = "e2e-correct-horse-battery";
    private const string Issuer = "https://localhost/";
    private const string Files = "https://files.example.test/";
    private const string FilesSignals = "https://files.example.test/signals/identity";
    private const string BffSignals = BffHost.Origin + "/api/identity/signal";
    private const string Service = "e2e-files-service";
    private const string ServiceSecret = "e2e-test-only-files-service-secret";

    private readonly SignalRouter _router = new();
    private Timer? _clock;
    private OidcHost _server = null!;
    private WebApplication _files = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _server = new OidcHost(
            new Dictionary<string, string?>
            {
                ["Identity:Server:Issuer"] = Issuer,
                ["Identity:Server:ServiceTokenLifetime"] = "00:00:04",
                ["Identity:Server:Resources:0:Name"] = "files",
                ["Identity:Server:Resources:0:Uri"] = Files,
                ["Identity:Server:Resources:0:Scopes:0"] = "files.read",
                ["Identity:Server:Resources:0:SignalUrl"] = FilesSignals,
                ["Identity:Server:Clients:0:RedirectUris:1"] = BffHost.Origin + "/api/identity/callback",
                ["Identity:Server:Clients:0:PostSignOutRedirectUris:1"] = BffHost.Origin + "/",
                ["Identity:Server:Clients:0:Grants:0"] = "authorization_code",
                ["Identity:Server:Clients:0:Grants:1"] = "token_exchange",
                ["Identity:Server:Clients:0:ExchangeResources:0"] = "files",
                ["Identity:Server:Clients:0:Scopes:0"] = "files.read",
                ["Identity:Server:Clients:0:SignalUrl"] = BffSignals,
                ["Identity:Server:Clients:1:ClientId"] = Service,
                ["Identity:Server:Clients:1:ClientSecret"] = ServiceSecret,
                ["Identity:Server:Clients:1:Grants:0"] = "client_credentials",
                ["Identity:Server:Clients:1:Resources:0"] = "files",
                ["Identity:Server:Clients:1:Scopes:0"] = "files.read",
                ["Identity:Server:Clients:1:CanIntrospect"] = "true",
            },
            services => services.AddHttpClient(WebhookDefaultConstants.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _router));

        // Building a client starts the host: migrations and the client seeding.
        using (_server.Host.CreateClient())
        {
        }

        // Real time on the server: token lifetimes here are seconds long and verifiers read the wall clock; retries wait on it too.
        _clock = new Timer(_ => TickClock(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(50));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddJwtBearerAuthentication(options =>
        {
            options.Audience = Files;
            options.ClockSkew = TimeSpan.Zero;
            options.TrustedIssuers.Add(new TrustedIssuer { Issuer = Issuer });
            options.BackchannelHttpHandler = _server.Host.Server.CreateHandler();
        });
        builder.Services.AddIdentitySignalReceiver(options =>
        {
            options.Audience = FilesSignals;
            options.TrustedIssuers.Add(new TrustedIssuer { Issuer = Issuer });
            options.BackchannelHttpHandler = _server.Host.Server.CreateHandler();
        });
        builder.Services.AddTokenDenyList();
        builder.Services.AddPersonOperations();
        builder.Services.AddCurrentPrincipal();
        builder.Services.AddAuthorization();
        builder.Services.AddLiveTokenCheck(options =>
        {
            options.IntrospectionEndpoint = new Uri(Issuer + "connect/introspect");
            options.ClientId = Service;
            options.ClientSecret = ServiceSecret;
            options.BackchannelHttpHandler = _server.Host.Server.CreateHandler();
        });

        _files = builder.Build();
        _files.UseAuthentication();
        _files.UseAuthorization();
        _files.MapIdentitySignals("/signals/identity");
        _files.MapGet("/whoami", (ICurrentPrincipal caller) => Results.Json(new { subject = caller.Subject, roles = caller.Roles.ToArray() })).RequireAuthorization();
        _files.MapPost("/danger", () => Results.Ok()).RequireAuthorization(policy => policy.RequireLiveToken());
        await _files.StartAsync();
        _router.Routes["files.example.test"] = _files.GetTestServer().CreateHandler();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_clock is not null)
            await _clock.DisposeAsync();

        await _files.DisposeAsync();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task SignOut_ShouldStopAnIssuedToken_OnTheDenyListPath_BeforeItExpires()
    {
        var person = await SignedInPersonAsync();
        var token = await ExchangeAsync(person.Client, person.Tokens.AccessToken);
        (await WhoamiAsync(token)).StatusCode.Should().Be(HttpStatusCode.OK, "an issued token verifies offline");

        await EndSessionAsync(person);

        (await StatusSoonAsync(token, HttpStatusCode.Unauthorized, TimeSpan.FromSeconds(3))).Should().BeTrue("the signal puts it on the deny list before its 4 seconds run out");
    }

    [Fact]
    public async Task RoleChange_ShouldGiveFreshClaims_WithNoSignIn_ThroughTheCallersOwnCache()
    {
        var person = await SignedInPersonAsync(OrganizationRoleConstants.Editor);
        var tokens = await person.Client.SignInToAppAsync();
        await using var caller = Caller(new SessionSubject(tokens.AccessToken));
        var http = caller.GetRequiredService<IHttpClientFactory>().CreateClient("files");
        (await RoleSoonAsync(http, "editor")).Should().Be("editor");

        await MemberAsync(person, (organizations, organization, user) => organizations.ChangeRoleAsync(organization, user, OrganizationRoleConstants.Viewer));

        (await RoleSoonAsync(http, "viewer")).Should().Be("viewer", "a 401 drops the cached token, and the next one has fresh claims");
        (await person.Client.Http.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.OK, "nobody signed in again");
    }

    [Fact]
    public async Task LostSignal_ShouldLeaveTheTokenGood_OnlyUntilItsLifetimeEnds()
    {
        _router.IsDown = true;
        var person = await SignedInPersonAsync();
        var token = await ExchangeAsync(person.Client, person.Tokens.AccessToken);

        await EndSessionAsync(person);

        (await WhoamiAsync(token)).StatusCode.Should().Be(HttpStatusCode.OK, "no signal reached this service");
        (await StatusSoonAsync(token, HttpStatusCode.Unauthorized, TimeSpan.FromSeconds(8))).Should().BeTrue("the token's own lifetime is the limit");
    }

    [Fact]
    public async Task Job_ShouldContinueOnSignOut_AndCancelOnAccessLoss()
    {
        var person = await SignedInPersonAsync(OrganizationRoleConstants.Editor);
        var tokens = await person.Client.SignInToAppAsync();
        var sid = (await person.Client.ValidateAsync(tokens.AccessToken, OidcHost.Audience)).GetClaim("sid").Value;
        var tracker = _files.Services.GetRequiredService<IPersonOperationTracker>();
        using var background = tracker.Begin(new PersonOperationDeclaration { Name = "export", Subject = person.Id.ToString("D"), OrganizationId = person.Organization.ToString("D") });
        using var live = tracker.Begin(new PersonOperationDeclaration { Name = "live-query", Subject = person.Id.ToString("D"), SessionId = sid, OnAccessLost = PersonOperationReaction.Continue });

        await EndSessionAsync(person with { Tokens = tokens });

        (await CancelledSoonAsync(live)).Should().BeTrue("a sid-tagged live operation cancels on sign-out");
        live.CancelReason.Should().Be(PersonOperationCancelReasonConstants.SessionEnded);
        background.CancellationToken.IsCancellationRequested.Should().BeFalse("accepted work continues on sign-out");

        await MemberAsync(person, (organizations, organization, user) => organizations.RemoveMemberAsync(organization, user));

        (await CancelledSoonAsync(background)).Should().BeTrue("access loss cancels");
        background.CancelReason.Should().Be(PersonOperationCancelReasonConstants.AccessLost);
    }

    [Fact]
    public async Task LiveCheck_ShouldRefuseADangerousAction_EvenWithTheSignalLost()
    {
        _router.IsDown = true;
        var person = await SignedInPersonAsync(OrganizationRoleConstants.Editor);
        var tokens = await person.Client.SignInToAppAsync();
        var token = await ExchangeAsync(person.Client, tokens.AccessToken);
        (await SendDangerAsync(token)).StatusCode.Should().Be(HttpStatusCode.OK);

        await MemberAsync(person, (organizations, organization, user) => organizations.ChangeRoleAsync(organization, user, OrganizationRoleConstants.Viewer));

        (await SendDangerAsync(token)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "the check asks what stands now");
        (await WhoamiAsync(token)).StatusCode.Should().Be(HttpStatusCode.OK, "every other request stays offline");
    }

    [Fact]
    public async Task Bff_ShouldRefreshOnAnAccessSignal_AndEndOnASessionSignal()
    {
        await using var app = await BffHost.StartAsync(_server, options =>
        {
            options.SignalUrl = BffSignals;
            options.BackchannelHttpHandler = _server.Host.Server.CreateHandler();
        });
        _router.Routes[BffHost.HostName] = app.CreateHandler();
        using var browser = new BffBrowser(_server, app);
        var email = $"bff-signal-{Guid.NewGuid():N}@example.test";
        var personId = await browser.SignUpAtTheInstanceAsync(email, Password);
        var owner = await OwnerOfNewOrganizationAsync();
        await MemberAsync(new Person(null!, personId, owner, null!), (organizations, organization, user) => organizations.AddMemberAsync(organization, user, OrganizationRoleConstants.Editor));
        await browser.SignInToTheAppAsync();
        (await browser.SessionAsync()).GetProperty("organization").GetProperty("role").GetString().Should().Be("editor");

        await MemberAsync(new Person(null!, personId, owner, null!), (organizations, organization, user) => organizations.ChangeRoleAsync(organization, user, OrganizationRoleConstants.Viewer));

        string? role = null;
        for (var attempt = 0; attempt < 100 && role != "viewer"; attempt++)
        {
            var session = await browser.SessionAsync();
            session.GetProperty("isAuthenticated").GetBoolean().Should().BeTrue("a role change signs nobody out");
            role = session.GetProperty("organization").GetProperty("role").GetString();
            await Task.Delay(100);
        }

        role.Should().Be("viewer");

        using var tokenAnswer = await browser.GetAsync("/api/token");
        var accessToken = (await tokenAnswer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        using var relyingParty = _server.CreateClient();
        var sid = (await relyingParty.ValidateAsync(accessToken, OidcHost.Audience)).GetClaim("sid").Value;
        (await browser.SendAsync(HttpMethod.Delete, $"{BffBrowser.IdentityOrigin}/api/account/manage/sessions/{sid}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var isSignedOut = false;
        for (var attempt = 0; attempt < 100 && !isSignedOut; attempt++)
        {
            using var me = await browser.GetAsync("/api/me");
            isSignedOut = me.StatusCode == HttpStatusCode.Unauthorized;
            await Task.Delay(50);
        }

        isSignedOut.Should().BeTrue("the session signal ends the BFF session at once");
    }

    /// <summary>
    /// Calls until the person's role reads <paramref name="expected"/>. A token issued in the second of a change can take a 401,
    /// since <c>iat</c> has whole seconds, and the caller's cache drops it for the next try.
    /// </summary>
    private static async Task<string?> RoleSoonAsync(HttpClient http, string expected)
    {
        string? role = null;
        for (var attempt = 0; attempt < 100 && role != expected; attempt++)
        {
            using var response = await http.GetAsync("/whoami");
            if (response.IsSuccessStatusCode)
                role = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("roles").EnumerateArray().Single().GetString();

            await Task.Delay(100);
        }

        return role;
    }

    private void TickClock()
    {
        var now = DateTimeOffset.UtcNow;
        if (now > _server.Host.Clock.GetUtcNow())
            _server.Host.Clock.SetUtcNow(now);
    }

    private async Task<Person> SignedInPersonAsync(string? role = null)
    {
        var client = _server.CreateClient();
        var id = await client.SignUpAndSignInAsync($"person-{Guid.NewGuid():N}@example.test", Password);
        var organization = Guid.Empty;
        if (role is not null)
        {
            organization = await OwnerOfNewOrganizationAsync();
            await MemberAsync(new Person(client, id, organization, null!), (organizations, org, user) => organizations.AddMemberAsync(org, user, role));
        }

        return new Person(client, id, organization, await client.SignInToAppAsync());
    }

    private async Task<Guid> OwnerOfNewOrganizationAsync()
    {
        using var owner = _server.CreateClient();
        var ownerId = await owner.SignUpAndSignInAsync($"owner-{Guid.NewGuid():N}@example.test", Password);
        await using var scope = _server.Host.Services.CreateAsyncScope();
        var user = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(ownerId))!;
        var organization = new IdentityOrganization { Name = "Signals", Slug = $"signals-{Guid.NewGuid():N}"[..20] };
        (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateAsync(organization, user)).IsSuccess.Should().BeTrue();
        return organization.Id;
    }

    private async Task MemberAsync(Person person, Func<OrganizationService, IdentityOrganization, IdentityUser, Task<IdentityResult>> change)
    {
        await using var scope = _server.Host.Services.CreateAsyncScope();
        var organizations = scope.ServiceProvider.GetRequiredService<OrganizationService>();
        var user = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(person.Id))!;
        (await change(organizations, (await organizations.FindByIdAsync(person.Organization))!, user)).IsSuccess.Should().BeTrue();
    }

    private static async Task EndSessionAsync(Person person)
    {
        var sid = (await person.Client.ValidateAsync(person.Tokens.AccessToken, OidcHost.Audience)).GetClaim("sid").Value;
        (await person.Client.Http.DeleteAsync($"/api/account/manage/sessions/{sid}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static async Task<string> ExchangeAsync(OidcTestClient client, string subject)
    {
        using var response = await client.TokenAsync(new()
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
            ["subject_token"] = subject,
            ["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
            ["resource"] = Files,
        });
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("access_token").GetString()!;
    }

    private ServiceProvider Caller(IOAuth2SubjectTokenSource subject)
    {
        var services = new ServiceCollection();
        services.AddSingleton(subject);
        services.AddHttpClient(OAuth2HttpClientNames.TokenEndpoint).ConfigurePrimaryHttpMessageHandler(() => _server.Host.Server.CreateHandler());
        services.AddHttpClient("files", client => client.BaseAddress = new Uri(Files))
            .ConfigurePrimaryHttpMessageHandler(() => _files.GetTestServer().CreateHandler())
            .AddOAuth2TokenExchange(options =>
            {
                options.TokenEndpoint = new Uri(Issuer + "connect/token");
                options.ClientId = OidcHost.ClientId;
                options.ClientSecret = OidcHost.ClientSecret;
                options.Resource = Files;
            });
        return services.BuildServiceProvider();
    }

    private async Task<HttpResponseMessage> WhoamiAsync(string token)
    {
        using var client = _files.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Authorization = new("Bearer", token);
        return await client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendDangerAsync(string token)
    {
        using var client = _files.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/danger");
        request.Headers.Authorization = new("Bearer", token);
        return await client.SendAsync(request);
    }

    private async Task<bool> StatusSoonAsync(string token, HttpStatusCode status, TimeSpan within)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (TimeProvider.System.GetElapsedTime(started) < within)
        {
            if ((await WhoamiAsync(token)).StatusCode == status)
                return true;

            await Task.Delay(50);
        }

        return false;
    }

    private static async Task<bool> CancelledSoonAsync(PersonOperationLease lease)
    {
        for (var attempt = 0; attempt < 100 && !lease.CancellationToken.IsCancellationRequested; attempt++)
            await Task.Delay(50);

        return lease.CancellationToken.IsCancellationRequested;
    }

    private sealed record Person(OidcTestClient Client, Guid Id, Guid Organization, TokenSet Tokens);

    private sealed class SessionSubject(string token) : IOAuth2SubjectTokenSource
    {
        public ValueTask<OAuth2SubjectToken?> GetAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult<OAuth2SubjectToken?>(new OAuth2SubjectToken { Token = token, SessionKey = "session" });
    }

    /// <summary>Routes each signal to the in-memory host its URL names; a target gone or down answers as a dead service does.</summary>
    private sealed class SignalRouter : HttpMessageHandler
    {
        public Dictionary<string, HttpMessageHandler> Routes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool IsDown { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (IsDown || !Routes.TryGetValue(request.RequestUri!.Host, out var route))
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            return await new HttpMessageInvoker(route, disposeHandler: false).SendAsync(request, cancellationToken);
        }
    }
}

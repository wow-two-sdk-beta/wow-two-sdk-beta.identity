using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;
using WoW.Two.Sdk.Beta.Be.Messaging.Webhooks;

namespace Identity.Tests.E2E.Oidc;

/// <summary>
/// What the real service sends: an ended session and a changed access reach each named signal URL as a POST signed with the
/// token key, verifiable through the JWKS, off the request. Introspection answers what stands now.
/// </summary>
public sealed class SignalSenderTests : IAsyncLifetime
{
    private const string Password = "e2e-correct-horse-battery";
    private const string Issuer = "https://localhost/";
    private const string Files = "https://files.example.test/";
    private const string FilesSignals = "https://files.example.test/signals/identity";
    private const string DeskSignals = "https://desk.example.test/signals/identity";
    private const string Reader = "e2e-reader";
    private const string ReaderSecret = "e2e-test-only-reader-secret";

    private readonly CapturedSignals _signals = new();
    private OidcHost _server = null!;

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        _server = new OidcHost(
            new Dictionary<string, string?>
            {
                ["Identity:Server:Issuer"] = Issuer,
                ["Identity:Server:Resources:0:Name"] = "files",
                ["Identity:Server:Resources:0:Uri"] = Files,
                ["Identity:Server:Resources:0:Scopes:0"] = "files.read",
                ["Identity:Server:Resources:0:SignalUrl"] = FilesSignals,
                ["Identity:Server:Clients:0:Grants:0"] = "authorization_code",
                ["Identity:Server:Clients:0:Grants:1"] = "token_exchange",
                ["Identity:Server:Clients:0:ExchangeResources:0"] = "files",
                ["Identity:Server:Clients:0:Scopes:0"] = "files.read",
                ["Identity:Server:Clients:0:SignalUrl"] = DeskSignals,
                ["Identity:Server:Clients:1:ClientId"] = Reader,
                ["Identity:Server:Clients:1:ClientSecret"] = ReaderSecret,
                ["Identity:Server:Clients:1:Grants:0"] = "client_credentials",
                ["Identity:Server:Clients:1:Resources:0"] = "files",
                ["Identity:Server:Clients:1:Scopes:0"] = "files.read",
                ["Identity:Server:Clients:1:CanIntrospect"] = "true",
            },
            services =>
            {
                // The service leaves erasure to a host that asks for it; this one does, to hear what it sends.
                services.AddScoped<UserPersonalDataService>();
                services.AddHttpClient(WebhookDefaultConstants.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _signals);
            });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task SessionEnd_ShouldReachEverySignalUrl_SignedWithTheTokenKey_AndBoundToItsTarget()
    {
        using var client = _server.CreateClient();
        var person = await client.SignUpAndSignInAsync($"signal-{Guid.NewGuid():N}@example.test", Password);
        var tokens = await client.SignInToAppAsync();
        var sid = (await client.ValidateAsync(tokens.AccessToken, OidcHost.Audience)).GetClaim("sid").Value;

        (await client.Http.DeleteAsync($"/api/account/manage/sessions/{sid}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var received = await _signals.WaitForAsync(nameof(SessionEndedEvent), 2);
        received.Select(signal => signal.Url).Should().BeEquivalentTo(FilesSignals, DeskSignals);
        var keys = (await client.GetKeysAsync()).GetSigningKeys();
        foreach (var signal in received)
        {
            var signature = await VerifyAsync(signal, keys);
            signature.GetClaim(SignalClaimTypeConstants.Event).Value.Should().Be(nameof(SessionEndedEvent));
            signature.Audiences.Should().Equal(signal.Url);
            using var body = JsonDocument.Parse(signal.Body);
            body.RootElement.GetProperty(nameof(SessionEndedEvent.SessionId)).GetString().Should().Be(sid);
            body.RootElement.GetProperty(nameof(SessionEndedEvent.Subject)).GetString().Should().Be(person.ToString("D"));
            body.RootElement.GetProperty(nameof(SessionEndedEvent.Issuer)).GetString().Should().Be(Issuer);
            body.RootElement.GetProperty(nameof(SessionEndedEvent.TokensExpireBy)).GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow);
        }
    }

    [Fact]
    public async Task RoleChangeAndRemoval_ShouldSignalTheirKind_AndEndNoSession()
    {
        using var owner = _server.CreateClient();
        var ownerId = await owner.SignUpAndSignInAsync($"owner-{Guid.NewGuid():N}@example.test", Password);
        var organization = await CreateOrganizationAsync(ownerId);
        using var member = _server.CreateClient();
        var memberId = await member.SignUpAndSignInAsync($"member-{Guid.NewGuid():N}@example.test", Password);
        await MemberAsync(organization, memberId, (organizations, org, person) => organizations.AddMemberAsync(org, person, OrganizationRoleConstants.Editor));
        (await _signals.WaitForAsync(nameof(AccessChangedEvent), 2)).Should().OnlyContain(signal => Kind(signal) == nameof(AccessChangeKind.Changed));
        _signals.Clear();

        await MemberAsync(organization, memberId, (organizations, org, person) => organizations.RemoveMemberAsync(org, person));

        (await _signals.WaitForAsync(nameof(AccessChangedEvent), 2)).Should().OnlyContain(signal => Kind(signal) == nameof(AccessChangeKind.Removed));
        (await member.Http.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.OK);
        _signals.All.Should().NotContain(signal => signal.Event == nameof(SessionEndedEvent));
    }

    [Fact]
    public async Task PasswordReset_ShouldSignalTheSession_WithNoRefreshOrLaterRequest()
    {
        using var client = _server.CreateClient();
        var email = $"reset-{Guid.NewGuid():N}@example.test";
        var person = await client.SignUpAndSignInAsync(email, Password);
        var tokens = await client.SignInToAppAsync();
        var sid = (await client.ValidateAsync(tokens.AccessToken, OidcHost.Audience)).GetClaim("sid").Value;
        await using (var scope = _server.Host.Services.CreateAsyncScope())
        {
            var user = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(person))!;
            var resetToken = scope.ServiceProvider.GetRequiredService<UserPasswordService>().IssuePasswordResetToken(user);
            using var anonymous = _server.CreateClient();
            using var reset = await anonymous.Http.PostAsJsonAsync("/api/account/reset-password", new { email, resetToken, newPassword = "e2e-a-brand-new-passphrase" });
            reset.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var received = await _signals.WaitForAsync(nameof(SessionEndedEvent), 2);
        received.Select(signal => signal.Url).Should().BeEquivalentTo(FilesSignals, DeskSignals);
        using var body = JsonDocument.Parse(received[0].Body);
        body.RootElement.GetProperty(nameof(SessionEndedEvent.SessionId)).GetString().Should().Be(sid);
    }

    [Fact]
    public async Task AccountDeletion_ShouldSignalItsSession_AndEachOrganization()
    {
        using var client = _server.CreateClient();
        var person = await client.SignUpAndSignInAsync($"erase-{Guid.NewGuid():N}@example.test", Password);
        var organization = await CreateOrganizationAsync(person);
        var tokens = await client.SignInToAppAsync();
        var sid = (await client.ValidateAsync(tokens.AccessToken, OidcHost.Audience)).GetClaim("sid").Value;

        await using (var scope = _server.Host.Services.CreateAsyncScope())
        {
            var user = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(person))!;
            await scope.ServiceProvider.GetRequiredService<UserPersonalDataService>().DeleteAsync(user);
        }

        var ended = await _signals.WaitForAsync(nameof(SessionEndedEvent), 2);
        using (var body = JsonDocument.Parse(ended[0].Body))
            body.RootElement.GetProperty(nameof(SessionEndedEvent.SessionId)).GetString().Should().Be(sid);
        var changed = await _signals.WaitForAsync(nameof(AccessChangedEvent), 2);
        changed.Should().OnlyContain(signal => Kind(signal) == nameof(AccessChangeKind.Removed));
        using var change = JsonDocument.Parse(changed[0].Body);
        change.RootElement.GetProperty(nameof(AccessChangedEvent.OrganizationId)).GetString().Should().Be(organization.ToString("D"));
    }

    [Fact]
    public async Task SlowOrFailingService_ShouldNotHoldTheSignOut()
    {
        _signals.Delay = TimeSpan.FromSeconds(2);
        using var client = _server.CreateClient();
        await client.SignUpAndSignInAsync($"slow-{Guid.NewGuid():N}@example.test", Password);
        var tokens = await client.SignInToAppAsync();
        var sid = (await client.ValidateAsync(tokens.AccessToken, OidcHost.Audience)).GetClaim("sid").Value;

        var started = TimeProvider.System.GetTimestamp();
        (await client.Http.DeleteAsync($"/api/account/manage/sessions/{sid}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        TimeProvider.System.GetElapsedTime(started).Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Introspection_ShouldAnswerWhatStandsNow_ForASessionAndARole()
    {
        using var owner = _server.CreateClient();
        var ownerId = await owner.SignUpAndSignInAsync($"own-{Guid.NewGuid():N}@example.test", Password);
        var organization = await CreateOrganizationAsync(ownerId);
        using var member = _server.CreateClient();
        var memberId = await member.SignUpAndSignInAsync($"live-{Guid.NewGuid():N}@example.test", Password);
        await MemberAsync(organization, memberId, (organizations, org, person) => organizations.AddMemberAsync(org, person, OrganizationRoleConstants.Editor));
        var tokens = await member.SignInToAppAsync();
        var exchanged = await ExchangeAsync(member, tokens.AccessToken);

        (await ActiveAsync(member, tokens.AccessToken)).Should().BeTrue();
        (await ActiveAsync(member, exchanged)).Should().BeTrue("an exchanged token has no row to look up");

        await MemberAsync(organization, memberId, (organizations, org, person) => organizations.ChangeRoleAsync(org, person, OrganizationRoleConstants.Viewer));
        (await ActiveAsync(member, tokens.AccessToken)).Should().BeFalse("the token still says editor");
        (await ActiveAsync(member, exchanged)).Should().BeFalse();

        var fresh = await TokenSet.ReadAsync(await member.RefreshAsync(tokens.RefreshToken));
        (await ActiveAsync(member, fresh.AccessToken)).Should().BeTrue("the refresh carries the new role with no sign-in");

        var sid = (await member.ValidateAsync(fresh.AccessToken, OidcHost.Audience)).GetClaim("sid").Value;
        (await member.Http.DeleteAsync($"/api/account/manage/sessions/{sid}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ActiveAsync(member, fresh.AccessToken)).Should().BeFalse("the session ended");
    }

    [Fact]
    public async Task Introspection_ShouldStandForAServiceToken_AndRefuseAClientWithoutThePermission()
    {
        using var client = _server.CreateClient();
        using var issued = await client.Http.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = Reader,
            ["client_secret"] = ReaderSecret,
            ["resource"] = Files,
        }));
        using var body = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
        var token = body.RootElement.GetProperty("access_token").GetString()!;

        (await ActiveAsync(client, token)).Should().BeTrue();

        using var refused = await client.Http.PostAsync("/connect/introspect", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = token,
            ["client_id"] = OidcHost.ClientId,
            ["client_secret"] = OidcHost.ClientSecret,
        }));
        refused.IsSuccessStatusCode.Should().BeFalse("this client was not granted introspection");
    }

    [Theory]
    [InlineData("https://other.example.test/")]
    [InlineData(Files)]
    public async Task TargetErrors_ShouldNameTheirRfcCode(string resource)
    {
        using var client = _server.CreateClient();
        await client.SignUpAndSignInAsync($"code-{Guid.NewGuid():N}@example.test", Password);
        var tokens = await client.SignInToAppAsync();

        using var response = await client.TokenAsync(new()
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
            ["subject_token"] = tokens.AccessToken,
            ["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
            ["resource"] = resource,
            ["scope"] = "files.nothing",
        });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("error").GetString().Should().BeOneOf("invalid_target", "invalid_scope");
    }

    private static string Kind(CapturedSignal signal)
    {
        using var body = JsonDocument.Parse(signal.Body);
        return body.RootElement.GetProperty(nameof(AccessChangedEvent.Kind)).ValueKind == JsonValueKind.Number
            ? ((AccessChangeKind)body.RootElement.GetProperty(nameof(AccessChangedEvent.Kind)).GetInt32()).ToString()
            : body.RootElement.GetProperty(nameof(AccessChangedEvent.Kind)).GetString()!;
    }

    private static async Task<JsonWebToken> VerifyAsync(CapturedSignal signal, IList<SecurityKey> keys)
    {
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(signal.Signature, new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = signal.Url,
            IssuerSigningKeys = keys,
            ValidTypes = [SignalClaimTypeConstants.TokenType],
        });
        result.IsValid.Should().BeTrue(result.Exception?.Message);
        var token = (JsonWebToken)result.SecurityToken;
        token.GetClaim(SignalClaimTypeConstants.BodyHash).Value.Should().Be(Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(signal.Body))));
        return token;
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

    private static async Task<bool> ActiveAsync(OidcTestClient client, string token)
    {
        using var response = await client.Http.PostAsync("/connect/introspect", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = token,
            ["token_type_hint"] = "access_token",
            ["client_id"] = Reader,
            ["client_secret"] = ReaderSecret,
        }));
        response.IsSuccessStatusCode.Should().BeTrue();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("active").GetBoolean();
    }

    private async Task<Guid> CreateOrganizationAsync(Guid ownerId)
    {
        await using var scope = _server.Host.Services.CreateAsyncScope();
        var owner = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(ownerId))!;
        var organization = new IdentityOrganization { Name = "Signals", Slug = $"signals-{Guid.NewGuid():N}"[..20] };
        (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateAsync(organization, owner)).IsSuccess.Should().BeTrue();
        return organization.Id;
    }

    private async Task MemberAsync(Guid organizationId, Guid userId, Func<OrganizationService, IdentityOrganization, IdentityUser, Task<IdentityResult>> change)
    {
        await using var scope = _server.Host.Services.CreateAsyncScope();
        var organizations = scope.ServiceProvider.GetRequiredService<OrganizationService>();
        var person = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(userId))!;
        (await change(organizations, (await organizations.FindByIdAsync(organizationId))!, person)).IsSuccess.Should().BeTrue();
    }

    private sealed record CapturedSignal(string Url, string Event, string Signature, string Body);

    private sealed class CapturedSignals : HttpMessageHandler
    {
        private readonly Lock _gate = new();
        private readonly List<CapturedSignal> _signals = [];

        public TimeSpan Delay { get; set; }

        public IReadOnlyList<CapturedSignal> All
        {
            get
            {
                lock (_gate)
                    return [.. _signals];
            }
        }

        public void Clear()
        {
            lock (_gate)
                _signals.Clear();
        }

        public async Task<IReadOnlyList<CapturedSignal>> WaitForAsync(string eventType, int count)
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                var matching = All.Where(signal => signal.Event == eventType).ToList();
                if (matching.Count >= count)
                    return matching;

                await Task.Delay(50);
            }

            throw new TimeoutException($"Only {All.Count} signals arrived, none enough of {eventType}.");
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Delay > TimeSpan.Zero)
                await Task.Delay(Delay, cancellationToken);

            var signal = new CapturedSignal(
                request.RequestUri!.AbsoluteUri,
                request.Headers.GetValues(WebhookHeaderConstants.Event).Single(),
                request.Headers.GetValues(WebhookHeaderConstants.Signature).Single(),
                await request.Content!.ReadAsStringAsync(cancellationToken));
            lock (_gate)
                _signals.Add(signal);

            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }
}

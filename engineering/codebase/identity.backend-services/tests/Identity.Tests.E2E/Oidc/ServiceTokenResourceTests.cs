using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2ClientCredentials;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2TokenExchange;
using WoW.Two.Sdk.Beta.Be.Identity.Authorization;
using WoW.Two.Sdk.Beta.Be.Identity.CurrentPrincipal;
using WoW.Two.Sdk.Beta.Be.Identity.CurrentUser;
using WoW.Two.Sdk.Beta.Be.Identity.Jwt;

namespace Identity.Tests.E2E.Oidc;

/// <summary>
/// The whole mechanism end to end: the real Identity service, the be sdk's caller handlers and real resource hosts that
/// verify offline against the service's JWKS. A token reaches its audience and no other, an exchanged token reaches a
/// resource as the person with <c>act</c>, an ended session is refused, and a client on <c>private_key_jwt</c> needs no
/// change in calling code.
/// </summary>
public sealed class ServiceTokenResourceTests : IAsyncLifetime
{
    private const string Password = "e2e-correct-horse-battery";
    private const string Issuer = "https://localhost/";
    private const string Desk = "https://desk.example.test/";
    private const string Files = "https://files.example.test/";
    private const string Worker = "e2e-worker";
    private const string WorkerSecret = "e2e-test-only-worker-secret";
    private const string KeyWorker = "e2e-key-worker";
    private const string KeyId = "e2e-key-1";

    private readonly RSA _clientKey = RSA.Create(2048);
    private OidcHost _server = null!;
    private WebApplication _desk = null!;
    private WebApplication _files = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var publicKey = _clientKey.ExportParameters(includePrivateParameters: false);
        var keySet = $$"""{ "keys": [ { "kty": "RSA", "use": "sig", "kid": "{{KeyId}}", "n": "{{Base64UrlEncoder.Encode(publicKey.Modulus)}}", "e": "{{Base64UrlEncoder.Encode(publicKey.Exponent)}}" } ] }""";
        _server = new OidcHost(new Dictionary<string, string?>
        {
            ["Identity:Server:Resources:0:Name"] = "desk",
            ["Identity:Server:Resources:0:Uri"] = Desk,
            ["Identity:Server:Resources:0:Scopes:0"] = "desk.media",
            ["Identity:Server:Resources:1:Name"] = "files",
            ["Identity:Server:Resources:1:Uri"] = Files,
            ["Identity:Server:Resources:1:Scopes:0"] = "files.read",
            ["Identity:Server:Clients:0:Grants:0"] = "authorization_code",
            ["Identity:Server:Clients:0:Grants:1"] = "token_exchange",
            ["Identity:Server:Clients:0:ExchangeResources:0"] = "files",
            ["Identity:Server:Clients:0:Scopes:0"] = "files.read",
            ["Identity:Server:Clients:1:ClientId"] = Worker,
            ["Identity:Server:Clients:1:ClientSecret"] = WorkerSecret,
            ["Identity:Server:Clients:1:Grants:0"] = "client_credentials",
            ["Identity:Server:Clients:1:Resources:0"] = "desk",
            ["Identity:Server:Clients:1:Scopes:0"] = "desk.media",
            ["Identity:Server:Clients:2:ClientId"] = KeyWorker,
            ["Identity:Server:Clients:2:JsonWebKeySet"] = keySet,
            ["Identity:Server:Clients:2:Grants:0"] = "client_credentials",
            ["Identity:Server:Clients:2:Resources:0"] = "desk",
            ["Identity:Server:Clients:2:Scopes:0"] = "desk.media",
        });

        // Building a client starts the host: migrations and the client seeding.
        using (_server.Host.CreateClient())
        {
        }

        _desk = await StartResourceAsync(Desk, "desk.media");
        _files = await StartResourceAsync(Files, "files.read");
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _desk.DisposeAsync();
        await _files.DisposeAsync();
        await _server.DisposeAsync();
        _clientKey.Dispose();
    }

    [Fact]
    public async Task ClientCredentialsToken_ShouldReachItsAudience_AsAService_AndBeRefusedByAnother()
    {
        await using var caller = Caller(services => services.AddHttpClient("desk", client => client.BaseAddress = new Uri(Desk))
            .ConfigurePrimaryHttpMessageHandler(() => _desk.GetTestServer().CreateHandler())
            .AddOAuth2ClientCredentials(WorkerOnDesk));
        var http = caller.GetRequiredService<IHttpClientFactory>().CreateClient("desk");

        var answer = await http.GetFromJsonAsync<JsonElement>("/whoami");

        answer.GetProperty("kind").GetString().Should().Be(nameof(PrincipalKind.Service));
        answer.GetProperty("subject").GetString().Should().Be(Worker);
        answer.GetProperty("clientId").GetString().Should().Be(Worker);
        answer.GetProperty("issuer").GetString().Should().Be(Issuer);
        answer.GetProperty("userKind").GetString().Should().Be(nameof(UserKind.Service));

        // The same token carried to another service stops at its audience check.
        var bearer = (await http.GetFromJsonAsync<JsonElement>("/token")).GetProperty("token").GetString();
        using var other = _files.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Authorization = new("Bearer", bearer);
        (await other.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ClientOnPrivateKeyJwt_ShouldNeedOnlyAConfigurationChange()
    {
        await using var caller = Caller(services => services.AddHttpClient("desk", client => client.BaseAddress = new Uri(Desk))
            .ConfigurePrimaryHttpMessageHandler(() => _desk.GetTestServer().CreateHandler())
            .AddOAuth2ClientCredentials(options =>
            {
                options.TokenEndpoint = new Uri(Issuer + "connect/token");
                options.ClientId = KeyWorker;
                options.ClientPrivateKey = _clientKey.ExportPkcs8PrivateKeyPem();
                options.ClientPrivateKeyId = KeyId;
                options.Resource = Desk;
            }));
        var http = caller.GetRequiredService<IHttpClientFactory>().CreateClient("desk");

        (await http.GetFromJsonAsync<JsonElement>("/whoami")).GetProperty("subject").GetString().Should().Be(KeyWorker);
    }

    [Fact]
    public async Task WrongClientKey_ShouldBeRefusedAtTheTokenEndpoint()
    {
        using var stranger = RSA.Create(2048);
        await using var caller = Caller(services => services.AddHttpClient("desk", client => client.BaseAddress = new Uri(Desk))
            .ConfigurePrimaryHttpMessageHandler(() => _desk.GetTestServer().CreateHandler())
            .AddOAuth2ClientCredentials(options =>
            {
                options.TokenEndpoint = new Uri(Issuer + "connect/token");
                options.ClientId = KeyWorker;
                options.ClientPrivateKey = stranger.ExportPkcs8PrivateKeyPem();
                options.Resource = Desk;
            }));
        var http = caller.GetRequiredService<IHttpClientFactory>().CreateClient("desk");

        (await FluentActions.Invoking(() => http.GetAsync("/whoami")).Should().ThrowAsync<OAuth2TokenRequestException>()).Which.Error.Should().Be("invalid_client");
    }

    [Fact]
    public async Task ExchangedToken_ShouldReachTheResourceAsThePerson_WithAct_UntilTheSessionEnds()
    {
        using var browser = _server.CreateClient();
        var person = await browser.SignUpAndSignInAsync($"act-{Guid.NewGuid():N}@example.test", Password);
        var tokens = await browser.SignInToAppAsync();
        var subject = new StaticSubjectTokens(tokens.AccessToken);
        await using var caller = Caller(services =>
        {
            services.AddSingleton<IOAuth2SubjectTokenSource>(subject);
            services.AddHttpClient("files", client => client.BaseAddress = new Uri(Files))
                .ConfigurePrimaryHttpMessageHandler(() => _files.GetTestServer().CreateHandler())
                .AddOAuth2TokenExchange(options =>
                {
                    options.TokenEndpoint = new Uri(Issuer + "connect/token");
                    options.ClientId = OidcHost.ClientId;
                    options.ClientSecret = OidcHost.ClientSecret;
                    options.Resource = Files;
                });
        });
        var http = caller.GetRequiredService<IHttpClientFactory>().CreateClient("files");

        var answer = await http.GetFromJsonAsync<JsonElement>("/whoami");

        answer.GetProperty("kind").GetString().Should().Be(nameof(PrincipalKind.Person));
        answer.GetProperty("subject").GetString().Should().Be(person.ToString("D"));
        answer.GetProperty("clientId").GetString().Should().Be(OidcHost.ClientId);
        answer.GetProperty("actor").GetString().Should().Be(OidcHost.ClientId);

        var sid = (await browser.ValidateAsync(tokens.AccessToken, OidcHost.Audience)).GetClaim("sid").Value;
        (await browser.Http.DeleteAsync($"/api/account/manage/sessions/{sid}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        subject.SessionKey = "after-sign-out";

        (await FluentActions.Invoking(() => http.GetAsync("/whoami")).Should().ThrowAsync<OAuth2TokenRequestException>()).Which.Error.Should().Be("invalid_grant");
    }

    [Fact]
    public async Task ScopePolicy_ShouldAnswer403_ForAScopeTheTokenDoesNotList()
    {
        await using var caller = Caller(services => services.AddHttpClient("desk", client => client.BaseAddress = new Uri(Desk))
            .ConfigurePrimaryHttpMessageHandler(() => _desk.GetTestServer().CreateHandler())
            .AddOAuth2ClientCredentials(WorkerOnDesk));
        var http = caller.GetRequiredService<IHttpClientFactory>().CreateClient("desk");

        (await http.GetAsync("/admin")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private void WorkerOnDesk(OAuth2ClientCredentialsOptions options)
    {
        options.TokenEndpoint = new Uri(Issuer + "connect/token");
        options.ClientId = Worker;
        options.ClientSecret = WorkerSecret;
        options.Resource = Desk;
    }

    /// <summary>A caller's container, its token endpoint routed to the Identity test host.</summary>
    private ServiceProvider Caller(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddHttpClient(OAuth2HttpClientNames.TokenEndpoint).ConfigurePrimaryHttpMessageHandler(() => _server.Host.Server.CreateHandler());
        configure(services);
        return services.BuildServiceProvider();
    }

    /// <summary>A resource server as a product would write it: trust one issuer, name its audience, require a scope.</summary>
    private async Task<WebApplication> StartResourceAsync(string audience, string scope)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddJwtBearerAuthentication(options =>
        {
            options.Audience = audience;
            options.TrustedIssuers.Add(new TrustedIssuer { Issuer = Issuer, MetadataAddress = new Uri(Issuer + ".well-known/openid-configuration") });
            options.BackchannelHttpHandler = _server.Host.Server.CreateHandler();
        });
        builder.Services.AddCurrentPrincipal();
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/whoami", (ICurrentPrincipal caller, ICurrentUserService user) => Results.Json(new
        {
            kind = caller.Kind.ToString(),
            subject = caller.Subject,
            clientId = caller.ClientId,
            issuer = caller.Issuer,
            actor = caller.Actor?.Subject,
            userKind = user.Kind.ToString(),
        })).RequireAuthorization(policy => policy.RequireScope(scope).RequireAudience(audience));
        app.MapGet("/admin", () => Results.Ok()).RequireAuthorization(policy => policy.RequireScope("desk.admin"));
        app.MapGet("/token", (HttpContext http) => Results.Json(new { token = http.Request.Headers.Authorization.ToString()["Bearer ".Length..] })).RequireAuthorization();
        await app.StartAsync();
        return app;
    }

    private sealed class StaticSubjectTokens(string token) : IOAuth2SubjectTokenSource
    {
        public string SessionKey { get; set; } = "session";

        public ValueTask<OAuth2SubjectToken?> GetAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult<OAuth2SubjectToken?>(new OAuth2SubjectToken { Token = token, SessionKey = SessionKey });
    }
}

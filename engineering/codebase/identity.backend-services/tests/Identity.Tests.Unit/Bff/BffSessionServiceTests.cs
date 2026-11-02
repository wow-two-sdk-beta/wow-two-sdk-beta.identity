using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Bff;
using WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Bff;

/// <summary>The session's life against a stand-in token endpoint: start, resolve, refresh inside the window, end past it or once refused.</summary>
public sealed class BffSessionServiceTests
{
    private const string Issuer = "https://id.example.test/";
    private const string ClientId = "desk";

    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly TokenEndpoint _endpoint = new();
    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "test-key" };
    private readonly IBffSessionRepository _repository = new DistributedCacheBffSessionRepository(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));

    [Fact]
    public async Task Start_ShouldKeyTheSessionOnTheSid_AndResolveItByItsReference()
    {
        var service = Service();

        var session = await service.StartAsync(Person("person-1"), Tokens(expiresIn: TimeSpan.FromMinutes(10)), CancellationToken.None);
        var resolved = await service.ResolveAsync(ReferenceOf(session), CancellationToken.None);

        session.SessionId.Should().Be("sid-1");
        session.State.ExpiresAt.Should().Be(_time.GetUtcNow().AddHours(12));
        (await _repository.FindAsync("sid-1")).Should().NotBeNull();
        resolved!.State.AccessToken.Should().Be("access-0");
        BffSessionService.CreatePrincipal(resolved).FindFirst("email")!.Value.Should().Be("person-1@example.test");
        _endpoint.Calls.Should().Be(0, "a fresh access token needs no refresh");
        (await service.ResolveAsync(new BffSessionReference { SessionId = "sid-1", Reference = "guessed" }, CancellationToken.None))
            .Should().BeNull("only the cookie of this sign-in opens the session");
    }

    [Fact]
    public async Task Resolve_ShouldRefreshNearTheAccessTokensEnd_AndTakeTheNewIdTokensClaims()
    {
        var service = Service();
        var session = await service.StartAsync(Person("person-1"), Tokens(expiresIn: TimeSpan.FromMinutes(10)), CancellationToken.None);
        _endpoint.Respond(HttpStatusCode.OK, new { access_token = "access-1", refresh_token = "refresh-1", expires_in = 600, id_token = IdToken("person-1", "Alice Renamed") });

        _time.Advance(TimeSpan.FromMinutes(9.5));
        var refreshed = await service.ResolveAsync(ReferenceOf(session), CancellationToken.None);

        _endpoint.Calls.Should().Be(1);
        _endpoint.LastForm.Should().Contain("grant_type=refresh_token").And.Contain("refresh_token=refresh-0").And.Contain("client_id=desk");
        refreshed!.State.AccessToken.Should().Be("access-1");
        refreshed.State.RefreshToken.Should().Be("refresh-1");
        refreshed.State.ExpiresAt.Should().Be(session.State.ExpiresAt, "a refresh never extends the session");
        BffSessionService.CreatePrincipal(refreshed).FindFirst("name")!.Value.Should().Be("Alice Renamed");
        BffSessionService.CreatePrincipal(refreshed).HasClaim(claim => claim.Type is "exp" or "aud" or "iss").Should().BeFalse("protocol claims are dropped");
        (await service.ResolveAsync(ReferenceOf(session), CancellationToken.None))!.State.AccessToken.Should().Be("access-1", "the store keeps the refresh");
        _endpoint.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Resolve_ShouldEndTheSession_OnceTheInstanceRefusesTheRefresh()
    {
        var service = Service();
        var session = await service.StartAsync(Person("person-1"), Tokens(expiresIn: TimeSpan.FromMinutes(10)), CancellationToken.None);
        _endpoint.Respond(HttpStatusCode.BadRequest, new { error = "invalid_grant" });

        _time.Advance(TimeSpan.FromMinutes(10));

        (await service.ResolveAsync(ReferenceOf(session), CancellationToken.None)).Should().BeNull();
        (await _repository.FindAsync("sid-1")).Should().BeNull("the session is removed");
    }

    [Fact]
    public async Task Resolve_ShouldEndTheSession_WhenTheRefreshNamesAnotherPerson()
    {
        var service = Service();
        var session = await service.StartAsync(Person("person-1"), Tokens(expiresIn: TimeSpan.FromMinutes(10)), CancellationToken.None);
        _endpoint.Respond(HttpStatusCode.OK, new { access_token = "access-1", expires_in = 600, id_token = IdToken("person-2", "Mallory") });

        _time.Advance(TimeSpan.FromMinutes(10));

        (await service.ResolveAsync(ReferenceOf(session), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Resolve_ShouldKeepTheSession_WhileTheInstanceIsUnreachable()
    {
        var service = Service();
        var session = await service.StartAsync(Person("person-1"), Tokens(expiresIn: TimeSpan.FromMinutes(10)), CancellationToken.None);
        _endpoint.Respond(HttpStatusCode.ServiceUnavailable, new { error = "temporarily_unavailable" });

        _time.Advance(TimeSpan.FromMinutes(10));

        (await service.ResolveAsync(ReferenceOf(session), CancellationToken.None))!.State.AccessToken.Should().Be("access-0");
    }

    [Fact]
    public async Task Session_ShouldEndWithTheInstancesSession_AsEachTokenResponseNamesIt()
    {
        var service = Service();
        var tokens = Tokens(expiresIn: TimeSpan.FromMinutes(10));
        BffSessionService.WriteInstanceEnd(tokens, _time.GetUtcNow().AddDays(5));
        var session = await service.StartAsync(Person("person-1"), tokens, CancellationToken.None);
        session.State.ExpiresAt.Should().Be(_time.GetUtcNow().AddDays(5), "the code exchange named the instance session's end");

        _time.Advance(TimeSpan.FromMinutes(9.5));
        var fiveDays = (long)TimeSpan.FromDays(5).TotalSeconds;
        _endpoint.Respond(HttpStatusCode.OK, new { access_token = "access-1", refresh_token = "refresh-1", expires_in = 600, refresh_token_expires_in = fiveDays });
        var slid = await service.ResolveAsync(ReferenceOf(session), CancellationToken.None);

        slid!.State.ExpiresAt.Should().Be(_time.GetUtcNow().AddDays(5), "a sliding instance session moved its end, and the BFF session follows");
        (await _repository.FindAsync("sid-1"))!.ExpiresAt.Should().Be(slid.State.ExpiresAt);
    }

    [Fact]
    public async Task Resolve_ShouldEndTheSession_PastItsAbsoluteWindow()
    {
        var service = Service();
        var session = await service.StartAsync(Person("person-1"), Tokens(expiresIn: TimeSpan.FromHours(13)), CancellationToken.None);

        _time.Advance(TimeSpan.FromHours(12));

        (await service.ResolveAsync(ReferenceOf(session), CancellationToken.None)).Should().BeNull();
        (await _repository.FindAsync("sid-1")).Should().BeNull();
        _endpoint.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Resolve_ShouldEndTheSession_WhenTheAccessTokenRunsOutWithoutARefreshToken()
    {
        var service = Service();
        var tokens = Tokens(expiresIn: TimeSpan.FromMinutes(10));
        tokens.StoreTokens(tokens.GetTokens().Where(token => token.Name != "refresh_token"));
        var session = await service.StartAsync(Person("person-1"), tokens, CancellationToken.None);

        _time.Advance(TimeSpan.FromMinutes(10));

        (await service.ResolveAsync(ReferenceOf(session), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Refresh_ShouldSaveTheRotatedTokens_EvenWhenTheBrowserLeavesMidRefresh()
    {
        var service = Service();
        var session = await service.StartAsync(Person("person-1"), Tokens(expiresIn: TimeSpan.FromMinutes(10)), CancellationToken.None);
        using var request = new CancellationTokenSource();
        _endpoint.Respond(HttpStatusCode.OK, new { access_token = "access-1", refresh_token = "refresh-1", expires_in = 600 });
        _endpoint.OnSend = request.Cancel;

        _time.Advance(TimeSpan.FromMinutes(9.5));
        await service.ResolveAsync(ReferenceOf(session), request.Token);
        _endpoint.OnSend = null;

        var resolved = await service.ResolveAsync(ReferenceOf(session), CancellationToken.None);
        resolved!.State.RefreshToken.Should().Be("refresh-1", "the instance spent refresh-0 as it answered, so its successor must be kept");
        _endpoint.Calls.Should().Be(1, "the saved tokens need no second refresh");
    }

    [Fact]
    public async Task Refresh_ShouldFetchANewSigningKey_WhenTheInstanceRotatedIt()
    {
        var rotated = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "rotated-key" };
        var manager = new RotatingConfigurationManager(Configuration(_key), Configuration(_key, rotated));
        var service = Service(manager);
        var session = await service.StartAsync(Person("person-1"), Tokens(expiresIn: TimeSpan.FromMinutes(10)), CancellationToken.None);
        _endpoint.Respond(HttpStatusCode.OK, new { access_token = "access-1", refresh_token = "refresh-1", expires_in = 600, id_token = IdToken("person-1", "Alice", rotated) });

        _time.Advance(TimeSpan.FromMinutes(9.5));
        var refreshed = await service.ResolveAsync(ReferenceOf(session), CancellationToken.None);

        refreshed.Should().NotBeNull("a signing-key rotation at the instance must not sign every session out");
        refreshed!.State.AccessToken.Should().Be("access-1");
        manager.Refreshes.Should().Be(1, "the unknown key asked discovery once for the new key set");
    }

    private static OpenIdConnectConfiguration Configuration(params SecurityKey[] keys)
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer, TokenEndpoint = "https://id.example.test/connect/token" };
        foreach (var key in keys)
            configuration.SigningKeys.Add(key);
        return configuration;
    }

    private BffSessionService Service(IConfigurationManager<OpenIdConnectConfiguration>? manager = null)
    {
        var handler = new OpenIdConnectOptions
        {
            ClientId = ClientId,
            ClientSecret = "test-only-secret",
            ConfigurationManager = manager ?? new StaticConfigurationManager<OpenIdConnectConfiguration>(Configuration(_key)),
            Backchannel = new HttpClient(_endpoint),
            MapInboundClaims = false,
        };
        handler.TokenValidationParameters.ValidAudience = ClientId;
        var monitor = new StaticMonitor<OpenIdConnectOptions>(handler);
        var options = Options.Create(new IdentityBffOptions { Authority = Issuer, ClientId = ClientId });
        return new BffSessionService(
            _repository,
            new BffSessionProtector(new EphemeralDataProtectionProvider()),
            new BffTokenClient(monitor, NullLogger<BffTokenClient>.Instance),
            options,
            _time,
            NullLogger<BffSessionService>.Instance);
    }

    private static ClaimsPrincipal Person(string subject) => new(new ClaimsIdentity(
        [new Claim("sub", subject), new Claim("sid", "sid-1"), new Claim("name", "Alice"), new Claim("email", subject + "@example.test")], "oidc", "name", "role"));

    private AuthenticationProperties Tokens(TimeSpan expiresIn)
    {
        var properties = new AuthenticationProperties();
        properties.StoreTokens(
        [
            new AuthenticationToken { Name = "access_token", Value = "access-0" },
            new AuthenticationToken { Name = "refresh_token", Value = "refresh-0" },
            new AuthenticationToken { Name = "id_token", Value = "id-0" },
            new AuthenticationToken { Name = "expires_at", Value = _time.GetUtcNow().Add(expiresIn).ToString("o") },
        ]);
        return properties;
    }

    private string IdToken(string subject, string name, SecurityKey? key = null) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = ClientId,
        IssuedAt = DateTime.UtcNow,
        NotBefore = DateTime.UtcNow,
        Expires = DateTime.UtcNow.AddMinutes(5),
        Claims = new Dictionary<string, object> { ["sub"] = subject, ["sid"] = "sid-1", ["name"] = name },
        SigningCredentials = new SigningCredentials(key ?? _key, SecurityAlgorithms.RsaSha256),
    });

    private static BffSessionReference ReferenceOf(BffSession session)
        => new() { SessionId = session.SessionId, Reference = session.State.Reference };

    /// <summary>A token endpoint answering one canned response, counting calls.</summary>
    private sealed class TokenEndpoint : HttpMessageHandler
    {
        private HttpStatusCode _status = HttpStatusCode.InternalServerError;
        private string _body = "{}";

        public int Calls { get; private set; }

        public string LastForm { get; private set; } = string.Empty;

        /// <summary>Runs as the request arrives, such as the browser leaving mid-refresh.</summary>
        public Action? OnSend { get; set; }

        public void Respond(HttpStatusCode status, object body)
        {
            _status = status;
            _body = JsonSerializer.Serialize(body);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastForm = await request.Content!.ReadAsStringAsync(cancellationToken);
            OnSend?.Invoke();
            return new HttpResponseMessage(_status) { Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    /// <summary>Discovery whose key set changes once a refresh is asked for, as after the instance rotates its signing key.</summary>
    private sealed class RotatingConfigurationManager(OpenIdConnectConfiguration before, OpenIdConnectConfiguration after) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public int Refreshes { get; private set; }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) => Task.FromResult(Refreshes > 0 ? after : before);

        public void RequestRefresh() => Refreshes++;
    }

    /// <summary>An options monitor over one instance.</summary>
    private sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}

using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Bff;
using WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;
using WoW.Two.Sdk.Beta.Be.Identity.Jwt;
using WoW.Two.Sdk.Beta.Be.Identity.Signals;
using WoW.Two.Sdk.Beta.Be.Identity.Tests.Jwt;
using WoW.Two.Sdk.Beta.Be.Identity.Tests.Signals;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Bff;

/// <summary>The BFF's reaction on a real pipeline, over a stand-in instance: the signal route, a session ended, a person's claims refreshed.</summary>
public sealed class BffSignalTests
{
    private const string Authority = "https://id.example.test";
    private const string Issuer = Authority + "/";
    private const string SignalUrl = "https://app.example.test/api/identity/signal";
    private const string Route = "/api/identity/signal";

    private readonly RsaSecurityKey _key = SignedTokens.NewKey();

    private SignalRequests Signals => new(_key, Issuer, SignalUrl, Route);

    [Fact]
    public async Task NoSignalUrl_ShouldMapNoRoute_AndStillBoot()
    {
        await using var host = await BffSignalHost.StartAsync(_key, signalUrl: null);

        using var response = await host.Client.SendAsync(Signals.Post(Signals.SessionEnded("sid-1", "person-1")));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task SignalUrl_FromTheSection_ShouldMapTheRoute()
    {
        await using var host = await BffSignalHost.StartAsync(_key, SignalUrl, viaSection: true);

        using var response = await host.Client.SendAsync(Signals.Post(Signals.SessionEnded("sid-1", "person-1")));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task EndedSession_ShouldEndHere_ThroughTheRealRoute_WithNoAntiforgeryHeader()
    {
        await using var host = await BffSignalHost.StartAsync(_key, SignalUrl);
        using var signedIn = await host.Client.PostAsync("/test/sign-in", null);
        (await host.Client.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        using var response = await host.Client.SendAsync(Signals.Post(Signals.SessionEnded("sid-1", "person-1")));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "the signal stays out of the antiforgery check, though the client carries a session cookie");
        await SignalCapture.UntilAsync(async () => await host.StoredAsync("sid-1") is null, "the session leaves the store");
        (await host.Client.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var repeat = await host.Client.SendAsync(Signals.Post(Signals.SessionEnded("sid-1", "person-1")));
        repeat.StatusCode.Should().Be(HttpStatusCode.Accepted, "ending a session twice is no error");
    }

    [Fact]
    public async Task OtherUnsafeRequests_ShouldStillNeedTheHeader()
    {
        await using var host = await BffSignalHost.StartAsync(_key, SignalUrl);
        using var signedIn = await host.Client.PostAsync("/test/sign-in", null);

        using var response = await host.Client.PostAsync("/api/desks", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Signal_ShouldAnswer401_ForAWrongAudienceOrIssuer_AndEndNothing()
    {
        await using var host = await BffSignalHost.StartAsync(_key, SignalUrl);
        using var signedIn = await host.Client.PostAsync("/test/sign-in", null);
        var signal = Signals.SessionEnded("sid-1", "person-1");
        var stranger = new SignalRequests(_key, "https://stranger.example.test/", SignalUrl, Route);

        using var audience = await host.Client.SendAsync(Signals.Post(signal, new SignalShape { Audience = "https://files.example.test/signals/identity" }));
        using var issuer = await host.Client.SendAsync(stranger.Post(stranger.SessionEnded("sid-1", "person-1")));
        using var key = await host.Client.SendAsync(new SignalRequests(SignedTokens.NewKey(_key.KeyId), Issuer, SignalUrl, Route).Post(signal));

        audience.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        issuer.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        key.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await host.StoredAsync("sid-1")).Should().NotBeNull();
    }

    [Fact]
    public async Task SignalFromAnotherIssuerTheHostTrusts_ShouldReachNoSession()
    {
        const string resourceIssuer = "https://tenant.example.test/";
        var tenantKey = SignedTokens.NewKey();
        await using var host = await BffSignalHost.StartAsync(_key, SignalUrl, receiver: options =>
        {
            options.Audience = "https://app.example.test/signals/identity";
            options.TrustedIssuers.Add(new TrustedIssuer { Issuer = resourceIssuer, SigningKeys = [SignedTokens.PublicOf(tenantKey)] });
        });
        using var signedIn = await host.Client.PostAsync("/test/sign-in", null);
        var tenant = new SignalRequests(tenantKey, resourceIssuer, SignalUrl, Route);
        var own = Signals;

        using var foreign = await host.Client.SendAsync(tenant.Post(tenant.SessionEnded("sid-1", "person-1")));
        using var change = await host.Client.SendAsync(tenant.Post(tenant.AccessChanged("person-1", "org-1", AccessChangeKind.Changed)));
        using var marker = await host.Client.SendAsync(own.Post(own.SessionEnded("sid-marker", "person-2")));

        foreign.StatusCode.Should().Be(HttpStatusCode.Accepted, "the host trusts it, as a resource");
        await SignalCapture.UntilAsync(() => host.Capture.Sessions.Any(ended => ended.SessionId == "sid-marker"), "the later signal arrives");
        (await host.StoredAsync("sid-1")).Should().NotBeNull("a sid means something only at its own issuer");
        (await host.AccessChangedAtAsync("person-1")).Should().BeNull();
    }

    [Fact]
    public async Task ChangedAccess_ShouldRefreshTheNextRequest_Once_AndKeepTheSession()
    {
        await using var host = await BffSignalHost.StartAsync(_key, SignalUrl);
        using var signedIn = await host.Client.PostAsync("/test/sign-in", null);
        (await host.Client.GetStringAsync("/api/token")).Should().Be("access-0");
        host.Instance.TokenCalls.Should().Be(0);

        using var response = await host.Client.SendAsync(Signals.Post(Signals.AccessChanged("person-1", "org-1", AccessChangeKind.Changed)));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await SignalCapture.UntilAsync(() => !host.Capture.Accesses.IsEmpty, "the signal reaches the handler");
        (await host.Client.GetStringAsync("/api/token")).Should().Be("access-1", "the next request refreshes first");
        (await host.Client.GetStringAsync("/api/token")).Should().Be("access-1");
        host.Instance.TokenCalls.Should().Be(1, "the marker is spent by one refresh");
    }

    [Fact]
    public async Task ChangedAccess_ShouldEndTheSession_WhenTheInstanceRefusesTheRefresh()
    {
        await using var host = await BffSignalHost.StartAsync(_key, SignalUrl);
        using var signedIn = await host.Client.PostAsync("/test/sign-in", null);
        host.Instance.RefusesRefresh = true;

        using var response = await host.Client.SendAsync(Signals.Post(Signals.AccessChanged("person-1", "org-1", AccessChangeKind.Removed)));
        await SignalCapture.UntilAsync(() => !host.Capture.Accesses.IsEmpty, "the signal reaches the handler");

        (await host.Client.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await host.StoredAsync("sid-1")).Should().BeNull();
    }

    [Fact]
    public async Task ChangedAccess_ShouldKeepTheSession_WithNoRefreshTokenToSpend()
    {
        await using var host = await BffSignalHost.StartAsync(_key, SignalUrl);
        using var signedIn = await host.Client.PostAsync("/test/sign-in?refresh=false", null);

        using var response = await host.Client.SendAsync(Signals.Post(Signals.AccessChanged("person-1", "org-1", AccessChangeKind.Changed)));
        await SignalCapture.UntilAsync(() => !host.Capture.Accesses.IsEmpty, "the signal reaches the handler");

        (await host.Client.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.OK, "a role change signs nobody out");
        host.Instance.TokenCalls.Should().Be(0);
    }

    [Fact]
    public async Task SignInAfterTheChange_ShouldNotRefresh()
    {
        await using var host = await BffSignalHost.StartAsync(_key, SignalUrl);
        using var response = await host.Client.SendAsync(Signals.Post(Signals.AccessChanged("person-1", "org-1", AccessChangeKind.Changed, at: DateTimeOffset.UtcNow.AddSeconds(-5))));
        await SignalCapture.UntilAsync(() => !host.Capture.Accesses.IsEmpty, "the signal reaches the handler");

        using var signedIn = await host.Client.PostAsync("/test/sign-in", null);

        (await host.Client.GetStringAsync("/api/token")).Should().Be("access-0");
        host.Instance.TokenCalls.Should().Be(0);
    }

    [Fact]
    public async Task AnInstanceClockAhead_ShouldStillSpendTheMarkerInOneRefresh()
    {
        await using var host = await BffSignalHost.StartAsync(_key, SignalUrl);
        using var signedIn = await host.Client.PostAsync("/test/sign-in", null);

        using var response = await host.Client.SendAsync(Signals.Post(Signals.AccessChanged("person-1", "org-1", AccessChangeKind.Changed, at: DateTimeOffset.UtcNow.AddMinutes(1))));
        await SignalCapture.UntilAsync(() => !host.Capture.Accesses.IsEmpty, "the signal reaches the handler");

        (await host.Client.GetStringAsync("/api/token")).Should().Be("access-1");
        (await host.Client.GetStringAsync("/api/token")).Should().Be("access-1");
        host.Instance.TokenCalls.Should().Be(1);
    }

    [Fact]
    public async Task SessionEndedWhileTheInstanceAnswersARefresh_ShouldStayEnded_AndRefuseTheRequest()
    {
        await using var host = await BffSignalHost.StartAsync(_key, SignalUrl);
        using var signedIn = await host.Client.PostAsync("/test/sign-in", null);
        await ChangeAccessAsync(host);
        var refreshing = host.Instance.HoldNextRefresh();
        var request = host.Client.GetAsync("/api/me");
        await refreshing.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        using var signal = await host.Sender.SendAsync(Signals.Post(Signals.SessionEnded("sid-1", "person-1")));
        signal.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await SignalCapture.UntilAsync(async () => await host.StoredAsync("sid-1") is null, "the signal ends the session");
        refreshing.Release.TrySetResult();

        using var response = await request.WaitAsync(TimeSpan.FromSeconds(10));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the session ended before the refresh came back");
        (await host.StoredAsync("sid-1")).Should().BeNull("the refresh's save must not bring it back");
    }

    [Fact]
    public async Task SessionEndedWhileARefreshSaves_ShouldStayEnded()
    {
        var store = new HoldingSessionStore();
        await using var host = await BffSignalHost.StartAsync(_key, SignalUrl, before: services => services.AddSingleton<IBffSessionRepository>(store));
        using var signedIn = await host.Client.PostAsync("/test/sign-in", null);
        await ChangeAccessAsync(host);
        store.HoldNextSave();
        var request = host.Client.GetAsync("/api/me");
        await store.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var ending = host.Services.GetRequiredService<BffSessionService>().EndAsync("sid-1", CancellationToken.None);
        store.ReleaseSave.TrySetResult();

        await ending.WaitAsync(TimeSpan.FromSeconds(10));
        using var response = await request.WaitAsync(TimeSpan.FromSeconds(10));
        (await host.StoredAsync("sid-1")).Should().BeNull("the end waits for the save it raced, then removes what it wrote");
    }

    [Fact]
    public async Task ChangeHeardDuringARefresh_ShouldRefreshAgainOnTheNextRequest()
    {
        await using var host = await BffSignalHost.StartAsync(_key, SignalUrl);
        using var signedIn = await host.Client.PostAsync("/test/sign-in", null);
        await ChangeAccessAsync(host);
        var refreshing = host.Instance.HoldNextRefresh();
        var request = host.Client.GetStringAsync("/api/token");
        await refreshing.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await ChangeAccessAsync(host);
        refreshing.Release.TrySetResult();

        (await request.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be("access-1");
        (await host.Client.GetStringAsync("/api/token")).Should().Be("access-2", "the instance may have written access-1's claims before the change");
        (await host.Client.GetStringAsync("/api/token")).Should().Be("access-2", "one more refresh spends the change");
        host.Instance.TokenCalls.Should().Be(2);
    }

    [Fact]
    public async Task LateChange_ShouldNotMoveTheMarkerBack()
    {
        await using var host = await BffSignalHost.StartAsync(_key, SignalUrl);
        var now = DateTimeOffset.UtcNow;
        var changes = host.Services.GetRequiredService<BffAccessChangeRepository>();

        await changes.RecordAsync("person-1", now, CancellationToken.None);
        await changes.RecordAsync("person-1", now.AddMinutes(-5), CancellationToken.None);

        (await changes.FindAsync("person-1", CancellationToken.None)).Should().Be(now);
    }

    [Fact]
    public async Task SessionStoredBeforeClaimsAsOf_ShouldCountFromItsSignIn()
    {
        await using var host = await BffSignalHost.StartAsync(_key, SignalUrl);
        using var signedIn = await host.Client.PostAsync("/test/sign-in", null);
        var service = host.Services.GetRequiredService<BffSessionService>();
        var protector = host.Services.GetRequiredService<BffSessionProtector>();
        var repository = host.Services.GetRequiredService<IBffSessionRepository>();
        var record = (await repository.FindAsync("sid-1"))!;
        var state = protector.Unprotect(record.Payload)!;
        var signedInAt = state.SignedInAt;
        await repository.SaveAsync(record with { Payload = protector.Protect(state with { ClaimsAsOf = null }) });
        var reference = new BffSessionReference { SessionId = "sid-1", Reference = state.Reference };

        await host.Services.GetRequiredService<BffAccessChangeRepository>().RecordAsync("person-1", signedInAt.AddMinutes(-1), CancellationToken.None);
        (await service.ResolveAsync(reference, CancellationToken.None))!.State.AccessToken.Should().Be("access-0", "the change came before the sign-in");

        await host.Services.GetRequiredService<BffAccessChangeRepository>().RecordAsync("person-1", signedInAt.AddMinutes(1), CancellationToken.None);
        (await service.ResolveAsync(reference, CancellationToken.None))!.State.AccessToken.Should().Be("access-1");
    }

    [Theory]
    [InlineData("https://id.example.test", "https://id.example.test/")]
    [InlineData("https://id.example.test/", "https://id.example.test/")]
    [InlineData("https://id.example.test/", "https://id.example.test")]
    [InlineData("https://id.example.test/tenant", "https://id.example.test/tenant/")]
    public void AuthorityIssuers_ShouldHoldEitherSpelling(string authority, string signed)
        => new IdentityBffOptions { Authority = authority }.AuthorityIssuers.Should().Contain(signed);

    [Fact]
    public void SignalUrl_ShouldBeAbsolute_AtBoot()
    {
        var failure = new IdentityBffOptionsValidator().Validate(null, new IdentityBffOptions { Authority = Issuer, ClientId = "desk", SignalUrl = "/api/identity/signal" });

        failure.Failed.Should().BeTrue();
        failure.FailureMessage.Should().Contain(nameof(IdentityBffOptions.SignalUrl));
    }

    /// <summary>Sends a changed access through the real route and waits until this host recorded it.</summary>
    private async Task ChangeAccessAsync(BffSignalHost host)
    {
        var before = await host.AccessChangedAtAsync("person-1");
        using var response = await host.Sender.SendAsync(Signals.Post(Signals.AccessChanged("person-1", "org-1", AccessChangeKind.Changed)));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await SignalCapture.UntilAsync(async () => await host.AccessChangedAtAsync("person-1") is { } at && at != before, "the change is recorded");
    }

    /// <summary>A BFF on a real pipeline, signed in by a test route as the code flow's callback would.</summary>
    private sealed class BffSignalHost(WebApplication app, HttpClient client, StandInInstance instance, SignalCapture capture) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        /// <summary>Gets a client as the instance is one: it carries no session cookie.</summary>
        public HttpClient Sender { get; } = new(app.GetTestServer().CreateHandler()) { BaseAddress = new Uri("https://app.example.test") };

        public IServiceProvider Services => app.Services;

        public StandInInstance Instance { get; } = instance;

        public SignalCapture Capture { get; } = capture;

        public static async Task<BffSignalHost> StartAsync(
            RsaSecurityKey key,
            string? signalUrl,
            bool viaSection = false,
            Action<IdentitySignalReceiverOptions>? receiver = null,
            Action<IServiceCollection>? before = null)
        {
            var instance = new StandInInstance(key);
            var capture = new SignalCapture();
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            var section = new Dictionary<string, string?>
            {
                ["Identity:Client:Authority"] = Authority,
                ["Identity:Client:ClientId"] = "desk",
                ["Identity:Client:ClientSecret"] = "test-only-secret",
            };
            if (viaSection)
                section["Identity:Client:SignalUrl"] = signalUrl;

            builder.Configuration.AddInMemoryCollection(section);
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            builder.Services.AddProblemDetails();
            before?.Invoke(builder.Services);
            builder.Services.AddIdentityBff(options =>
            {
                options.BackchannelHttpHandler = instance;
                if (!viaSection)
                    options.SignalUrl = signalUrl;
            });
            if (receiver is not null)
                builder.Services.AddIdentitySignalReceiver(receiver);

            builder.Services.AddSingleton<Messaging.IEventHandler<SessionEndedEvent>>(capture);
            builder.Services.AddSingleton<Messaging.IEventHandler<AccessChangedEvent>>(capture);

            var app = builder.Build();
            app.UseIdentityBff();
            app.MapIdentityBffEndpoints();
            app.MapPost("/test/sign-in", SignInAsync);
            var api = app.MapGroup("/api");
            api.MapGet("/me", (ClaimsPrincipal user) => user.FindFirst("sub")!.Value).RequireAuthorization();
            api.MapGet("/token", async (HttpContext http) => await http.GetTokenAsync("access_token") ?? "none").RequireAuthorization();
            api.MapPost("/desks", () => "ok").RequireAuthorization();
            await app.StartAsync();

            var client = new HttpClient(new CookieContainerHandler { InnerHandler = app.GetTestServer().CreateHandler() })
            {
                BaseAddress = new Uri("https://app.example.test"),
            };
            return new BffSignalHost(app, client, instance, capture);
        }

        public Task<BffSessionRecord?> StoredAsync(string sessionId) => Services.GetRequiredService<IBffSessionRepository>().FindAsync(sessionId);

        public Task<DateTimeOffset?> AccessChangedAtAsync(string subject) => Services.GetRequiredService<BffAccessChangeRepository>().FindAsync(subject, CancellationToken.None);

        private static async Task SignInAsync(HttpContext http)
        {
            var identity = new ClaimsIdentity([new Claim("sub", "person-1"), new Claim("sid", "sid-1"), new Claim("name", "Alice")], "oidc", "name", "role");
            var tokens = new List<AuthenticationToken>
            {
                new() { Name = "access_token", Value = "access-0" },
                new() { Name = "id_token", Value = "id-0" },
                new() { Name = "expires_at", Value = DateTimeOffset.UtcNow.AddMinutes(10).ToString("o") },
            };
            if (http.Request.Query["refresh"] != "false")
                tokens.Add(new AuthenticationToken { Name = "refresh_token", Value = "refresh-0" });

            var properties = new AuthenticationProperties();
            properties.StoreTokens(tokens);
            await http.SignInAsync(BffAuthenticationSchemeConstants.Cookie, new ClaimsPrincipal(identity), properties);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            Sender.Dispose();
            await app.DisposeAsync();
            Instance.Dispose();
        }
    }

    /// <summary>The instance's discovery, its keys and its token endpoint; a refresh returns the next access token and no new ID token.</summary>
    private sealed class StandInInstance(RsaSecurityKey key) : HttpMessageHandler
    {
        private int _tokenCalls;
        private volatile RefreshHold? _hold;

        public int TokenCalls => _tokenCalls;

        public bool RefusesRefresh { get; set; }

        /// <summary>Makes the next token call wait, so a test can act while a refresh is under way.</summary>
        public RefreshHold HoldNextRefresh() => _hold = new RefreshHold();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/.well-known/openid-configuration":
                    return Json(new Dictionary<string, string>
                    {
                        ["issuer"] = Issuer,
                        ["authorization_endpoint"] = Authority + "/connect/authorize",
                        ["token_endpoint"] = Authority + "/connect/token",
                        ["jwks_uri"] = Authority + "/.well-known/jwks",
                    });
                case "/.well-known/jwks":
                    var parameters = key.Rsa.ExportParameters(includePrivateParameters: false);
                    return Json(new Dictionary<string, object>
                    {
                        ["keys"] = new[]
                        {
                            new Dictionary<string, string>
                            {
                                ["kty"] = "RSA",
                                ["use"] = "sig",
                                ["alg"] = SecurityAlgorithms.RsaSha256,
                                ["kid"] = key.KeyId,
                                ["n"] = Base64UrlEncoder.Encode(parameters.Modulus),
                                ["e"] = Base64UrlEncoder.Encode(parameters.Exponent),
                            },
                        },
                    });
                case "/connect/token":
                    if (RefusesRefresh)
                    {
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                        {
                            Content = new StringContent("""{"error":"invalid_grant"}""", Encoding.UTF8, "application/json"),
                        });
                    }

                    return RefreshAsync(cancellationToken);
                default:
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }

        private async Task<HttpResponseMessage> RefreshAsync(CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _tokenCalls);
            if (Interlocked.Exchange(ref _hold, null) is { } hold)
            {
                hold.Entered.TrySetResult();
                await hold.Release.Task.WaitAsync(cancellationToken);
            }

            return await Json(new Dictionary<string, object>
            {
                ["access_token"] = $"access-{call}",
                ["refresh_token"] = $"refresh-{call}",
                ["expires_in"] = 600,
            });
        }

        private static Task<HttpResponseMessage> Json<TBody>(TBody body)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            });
    }

    /// <summary>One held refresh: when it reached the instance, and what lets it answer.</summary>
    private sealed class RefreshHold
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>A session store whose next save waits, so a test can act between a refresh's last read and its write.</summary>
    private sealed class HoldingSessionStore : IBffSessionRepository
    {
        private readonly ConcurrentDictionary<string, BffSessionRecord> _records = new(StringComparer.Ordinal);
        private volatile bool _isHolding;

        public TaskCompletionSource SaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void HoldNextSave() => _isHolding = true;

        public Task<BffSessionRecord?> FindAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<BffSessionRecord?>(_records.GetValueOrDefault(sessionId));

        public async Task SaveAsync(BffSessionRecord record, CancellationToken cancellationToken = default)
        {
            if (_isHolding)
            {
                _isHolding = false;
                SaveEntered.TrySetResult();
                await ReleaseSave.Task.WaitAsync(cancellationToken);
            }

            _records[record.SessionId] = record;
        }

        public Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            _records.TryRemove(sessionId, out _);
            return Task.CompletedTask;
        }
    }
}

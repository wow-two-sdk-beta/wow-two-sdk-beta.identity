using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;
using WoW.Two.Sdk.Beta.Be.Identity.Jwt;
using WoW.Two.Sdk.Beta.Be.Identity.OidcServer;
using WoW.Two.Sdk.Beta.Be.Identity.Signals;
using WoW.Two.Sdk.Beta.Be.Identity.Tests.Jwt;
using WoW.Two.Sdk.Beta.Be.Messaging;
using WoW.Two.Sdk.Beta.Be.Messaging.Buses;
using WoW.Two.Sdk.Beta.Be.Messaging.Models;
using WoW.Two.Sdk.Beta.Be.Messaging.Transport;
using WoW.Two.Sdk.Beta.Be.Messaging.Webhooks;
using WoW.Two.Sdk.Beta.Be.Messaging.Webhooks.Inbound;
using WoW.Two.Sdk.Beta.Be.Messaging.Webhooks.Issuers;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Signals;

/// <summary>The signal route on a real pipeline: what passes the signature, what reaches the bus, and how often.</summary>
public sealed class IdentitySignalReceiverTests
{
    private const string Issuer = "https://id.example.test/";
    private const string Target = "https://files.example.test/signals/identity";
    private const string Route = "/signals/identity";

    private readonly RsaSecurityKey _key = SignedTokens.NewKey();
    private readonly SignalCapture _capture = new();
    private IServiceProvider _provider = null!;

    private SignalRequests Signals => new(_key, Issuer, Target, Route);

    [Fact]
    public async Task ValidSignal_ShouldAnswer202_AndReachTheHandlers_OfEitherEvent()
    {
        await using var host = await StartAsync();
        var ended = Signals.SessionEnded("sid-1", "person-1");
        var changed = Signals.AccessChanged("person-1", "org-1", AccessChangeKind.Removed);

        using var first = await host.Client.SendAsync(Signals.Post(ended));
        using var second = await host.Client.SendAsync(Signals.Post(changed));

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await SignalCapture.UntilAsync(() => !_capture.Sessions.IsEmpty && !_capture.Accesses.IsEmpty, "both signals reach the bus");
        _capture.Sessions.Single().Should().BeEquivalentTo(ended);
        _capture.Accesses.Single().Should().BeEquivalentTo(changed, "the numeric Kind the publisher writes reads back");
    }

    [Fact]
    public async Task SignalKeys_ShouldComeFromTheIssuersDiscovery_OverTheBackchannel()
    {
        using var discovery = new DiscoveryStub("https://id.example.test");
        discovery.Publish(_key);
        await using var host = await StartAsync(options =>
        {
            options.TrustedIssuers.Clear();
            options.TrustedIssuers.Add(new TrustedIssuer { Issuer = Issuer });
            options.BackchannelHttpHandler = discovery;
        });

        using var response = await host.Client.SendAsync(Signals.Post(Signals.SessionEnded("sid-1", "person-1")));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        discovery.MetadataFetches.Should().Be(1);
    }

    [Fact]
    public async Task DiscoveryOutage_ShouldAnswer503_SoTheSenderRetries()
    {
        using var discovery = new DiscoveryStub("https://id.example.test") { IsDown = true };
        discovery.Publish(_key);
        await using var host = await StartAsync(options =>
        {
            options.TrustedIssuers.Clear();
            options.TrustedIssuers.Add(new TrustedIssuer { Issuer = Issuer });
            options.BackchannelHttpHandler = discovery;
        });

        using (var down = await host.Client.SendAsync(Signals.Post(Signals.SessionEnded("sid-1", "person-1"))))
            down.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "a 401 would lose the signal; a 5xx is retried");
    }

    [Fact]
    public async Task RotatedKey_ShouldAnswer503_WhileDiscoveryCantShowIt_SoTheSenderRetries()
    {
        using var discovery = new DiscoveryStub("https://id.example.test");
        discovery.Publish(_key);
        await using var host = await StartAsync(options =>
        {
            options.TrustedIssuers.Clear();
            options.TrustedIssuers.Add(new TrustedIssuer { Issuer = Issuer });
            options.BackchannelHttpHandler = discovery;
        });
        using (var known = await host.Client.SendAsync(Signals.Post(Signals.SessionEnded("sid-1", "person-1"))))
            known.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var rotated = new SignalRequests(SignedTokens.NewKey(), Issuer, Target, Route);

        discovery.IsDown = true;
        using (var failing = await host.Client.SendAsync(rotated.Post(rotated.SessionEnded("sid-2", "person-1"))))
            failing.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "the refresh failed and the old keys came back without an error");

        discovery.IsDown = false;
        using (var early = await host.Client.SendAsync(rotated.Post(rotated.SessionEnded("sid-3", "person-1"))))
            early.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "inside the minimum between refreshes nothing is asked");
    }

    [Fact]
    public async Task RotatedKey_ShouldPass_OnceDiscoveryShowsIt()
    {
        using var discovery = new DiscoveryStub("https://id.example.test");
        discovery.Publish(_key);
        await using var host = await StartAsync(options =>
        {
            options.TrustedIssuers.Clear();
            options.TrustedIssuers.Add(new TrustedIssuer { Issuer = Issuer });
            options.BackchannelHttpHandler = discovery;
        });
        using (var known = await host.Client.SendAsync(Signals.Post(Signals.SessionEnded("sid-1", "person-1"))))
            known.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var key = SignedTokens.NewKey();
        var rotated = new SignalRequests(key, Issuer, Target, Route);
        discovery.Publish(_key, key);

        using var response = await host.Client.SendAsync(rotated.Post(rotated.SessionEnded("sid-2", "person-1")));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task AKnownKeyIdWithAnotherKey_ShouldAnswer401_ThroughDiscovery()
    {
        using var discovery = new DiscoveryStub("https://id.example.test");
        discovery.Publish(_key);
        await using var host = await StartAsync(options =>
        {
            options.TrustedIssuers.Clear();
            options.TrustedIssuers.Add(new TrustedIssuer { Issuer = Issuer });
            options.BackchannelHttpHandler = discovery;
        });
        var forged = new SignalRequests(SignedTokens.NewKey(_key.KeyId), Issuer, Target, Route);

        using var response = await host.Client.SendAsync(forged.Post(forged.SessionEnded("sid-1", "person-1")));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a key the issuer lists didn't sign it, and no refresh changes that");
    }

    [Fact]
    public async Task Signature_ShouldAnswer401_WhenItFailsAnyCheck_AndReachNoHandler()
    {
        await using var host = await StartAsync();
        var body = Signals.SessionEnded("sid-1", "person-1");
        var stranger = new SignalRequests(SignedTokens.NewKey(), "https://stranger.example.test/", Target, Route);
        var otherIssuerBody = body with { Issuer = "https://other.example.test/" };

        var refused = new Dictionary<string, HttpRequestMessage>
        {
            ["a wrong key"] = new SignalRequests(SignedTokens.NewKey(), Issuer, Target, Route).Post(body),
            ["a wrong audience"] = Signals.Post(body, new SignalShape { Audience = "https://desk.example.test/signals/identity" }),
            ["the audience with a trailing slash"] = Signals.Post(body, new SignalShape { Audience = Target + "/" }),
            ["an unknown issuer"] = stranger.Post(stranger.SessionEnded("sid-1", "person-1")),
            ["an altered body"] = Signals.Post(body, new SignalShape { SentBody = """{"Issuer":"https://id.example.test/","SessionId":"sid-2","Subject":"person-1","At":"2026-11-05T00:00:00Z"}""" }),
            ["a hash of something else"] = Signals.Post(body, new SignalShape { BodyHash = "AAAA" }),
            ["a signed event differing from the header"] = Signals.Post(body, new SignalShape { SignedEvent = nameof(AccessChangedEvent) }),
            ["a header differing from the signed event"] = Signals.Post(body, new SignalShape { EventHeader = nameof(AccessChangedEvent) }),
            ["a body issuer differing from iss"] = Signals.Post(otherIssuerBody),
            ["an access token's typ"] = Signals.Post(body, new SignalShape { TokenType = "at+jwt" }),
            ["an expired signature"] = Signals.Post(body, new SignalShape { SignedAt = DateTimeOffset.UtcNow.AddMinutes(-20), Expires = DateTimeOffset.UtcNow.AddMinutes(-10) }),
            ["a signature not yet valid"] = Signals.Post(body, new SignalShape { SignedAt = DateTimeOffset.UtcNow.AddMinutes(10) }),
            ["a body that isn't an object"] = Signals.Post(nameof(SessionEndedEvent), "[1]"u8.ToArray()),
            ["a body naming no issuer"] = Signals.Post(nameof(SessionEndedEvent), "{}"u8.ToArray()),
        };

        foreach (var (reason, request) in refused)
        {
            using var response = await host.Client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, reason);
        }

        using var unsigned = await host.Client.PostAsync(Route, new ByteArrayContent(Encoding.UTF8.GetBytes("{}")));
        unsigned.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "no signature at all");

        await AssertQuietAsync(host);
    }

    [Fact]
    public async Task IssuerAudiences_ShouldNotWidenWhatASignalMayName()
    {
        await using var host = await StartAsync(options =>
        {
            options.TrustedIssuers.Clear();
            options.TrustedIssuers.Add(new TrustedIssuer
            {
                Issuer = Issuer,
                SigningKeys = [SignedTokens.PublicOf(_key)],
                Audiences = ["https://files.example.test/"],
            });
        });
        var body = Signals.SessionEnded("sid-1", "person-1");

        using var tokenAudience = await host.Client.SendAsync(Signals.Post(body, new SignalShape { Audience = "https://files.example.test/" }));
        using var signalUrl = await host.Client.SendAsync(Signals.Post(body));

        tokenAudience.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "an issuer's audiences describe its tokens");
        signalUrl.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task ResolvedIssuer_ShouldBeHeard_WhileTheHostRuns_WithTheSignalUrlItsOnlyAudience()
    {
        const string tenantIssuer = "https://tenant.example.test/";
        var tenantKey = SignedTokens.NewKey();
        var tenant = new SignalRequests(tenantKey, tenantIssuer, Target, Route);
        var store = new TrustedIssuerStore();
        await using var host = await StartAsync(extra: services => services.AddSingleton(store).AddScoped<ITrustedIssuerResolver, StoreIssuerResolver>());
        var body = tenant.SessionEnded("sid-1", "person-1");

        using (var before = await host.Client.SendAsync(tenant.Post(body)))
            before.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the store doesn't know the tenant yet");
        store.Issuers[tenantIssuer] = new TrustedIssuer
        {
            Issuer = tenantIssuer,
            SigningKeys = [SignedTokens.PublicOf(tenantKey)],
            Audiences = ["https://files.example.test/"],
            Partition = "tenant",
        };
        using var tokenAudience = await host.Client.SendAsync(tenant.Post(body, new SignalShape { Audience = "https://files.example.test/" }));
        using var signalUrl = await host.Client.SendAsync(tenant.Post(body));

        tokenAudience.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a resolved issuer's audiences describe its tokens too");
        signalUrl.StatusCode.Should().Be(HttpStatusCode.Accepted, "a tenant added at runtime is heard with no restart");
        await SignalCapture.UntilAsync(() => !_capture.Sessions.IsEmpty, "the signal reaches the bus");
        _capture.Sessions.Single().Should().BeEquivalentTo(body);
    }

    [Fact]
    public async Task ResolvedIssuer_ShouldAnswer503_OnAKeyItsDiscoveryDoesntListYet()
    {
        using var discovery = new DiscoveryStub("https://id.example.test");
        discovery.Publish(_key);
        var store = new TrustedIssuerStore();
        store.Issuers[Issuer] = new TrustedIssuer { Issuer = Issuer };
        await using var host = await StartAsync(
            options =>
            {
                options.TrustedIssuers.Clear();
                options.BackchannelHttpHandler = discovery;
            },
            extra: services => services.AddSingleton(store).AddScoped<ITrustedIssuerResolver, StoreIssuerResolver>());
        var rotated = new SignalRequests(SignedTokens.NewKey(), Issuer, Target, Route);

        using var known = await host.Client.SendAsync(Signals.Post(Signals.SessionEnded("sid-1", "person-1")));
        using var unlisted = await host.Client.SendAsync(rotated.Post(rotated.SessionEnded("sid-2", "person-1")));

        known.StatusCode.Should().Be(HttpStatusCode.Accepted, "a resolver alone stands in for listed issuers");
        unlisted.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "a resolved issuer's rotation is retried like a listed one's");
    }

    [Fact]
    public async Task RepeatedDeliveryId_ShouldAnswerAsTheFirst_AndBeHandledOnce()
    {
        await using var host = await StartAsync();
        var signal = Signals.SessionEnded("sid-1", "person-1");
        var delivery = new SignalShape { DeliveryId = "delivery-1" };

        using var first = await host.Client.SendAsync(Signals.Post(signal, delivery));
        using var repeat = await host.Client.SendAsync(Signals.Post(signal, delivery));
        using var next = await host.Client.SendAsync(Signals.Post(Signals.SessionEnded("sid-2", "person-1")));

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        repeat.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await SignalCapture.UntilAsync(() => _capture.Sessions.Any(ended => ended.SessionId == "sid-2"), "the next signal arrives after the repeat");
        _capture.Sessions.Count(ended => ended.SessionId == "sid-1").Should().Be(1);
    }

    [Fact]
    public async Task UnknownEventType_ShouldAnswer202_AndBeIgnored_SoANewerSenderDoesNotFail()
    {
        await using var host = await StartAsync();
        var body = """{"Issuer":"https://id.example.test/","Whatever":1}"""u8.ToArray();

        using var response = await host.Client.SendAsync(Signals.Post("PasswordChangedEvent", body));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await AssertQuietAsync(host);
    }

    [Fact]
    public async Task KnownEvent_ShouldAnswer400_WhenItsSignedBodyLacksTheEventsFields()
    {
        await using var host = await StartAsync();
        var body = """{"Issuer":"https://id.example.test/"}"""u8.ToArray();

        using var response = await host.Client.SendAsync(Signals.Post(nameof(SessionEndedEvent), body));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OversizedBody_ShouldAnswer413()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.SendAsync(Signals.Post(nameof(SessionEndedEvent), new byte[(1024 * 1024) + 1]));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Lifetime_ShouldGoByTheReceiversClock()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(1));
        await using var host = await StartAsync(extra: services => services.AddSingleton<TimeProvider>(clock));

        using var fresh = await host.Client.SendAsync(Signals.Post(Signals.SessionEnded("sid-1", "person-1"), new SignalShape { SignedAt = clock.GetUtcNow() }));
        using var stale = await host.Client.SendAsync(Signals.Post(Signals.SessionEnded("sid-2", "person-1")));

        fresh.StatusCode.Should().Be(HttpStatusCode.Accepted);
        stale.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "signed an hour ago by this host's clock");
    }

    [Fact]
    public async Task Registration_ShouldBeSafeToRepeat_BesideAHostsOwnBus()
    {
        await using var host = await StartAsync(extra: services =>
        {
            services.AddInMemoryEventBus(typeof(SessionEndedEvent).Assembly);
            services.AddIdentitySignalReceiver();
            services.AddTokenDenyList();
            services.AddInMemoryEventBus(typeof(SessionEndedEvent).Assembly);
        });

        using var response = await host.Client.SendAsync(Signals.Post(Signals.SessionEnded("sid-1", "person-1")));
        using var next = await host.Client.SendAsync(Signals.Post(Signals.SessionEnded("sid-2", "person-1")));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await SignalCapture.UntilAsync(() => _capture.Sessions.Any(ended => ended.SessionId == "sid-2"), "the signals reach the bus");
        _capture.Sessions.Count(ended => ended.SessionId == "sid-1").Should().Be(1);
        _provider.GetServices<IHostedService>().Count(service => service.GetType().Name == "TransportConsumerBackgroundService").Should().Be(1);
        _provider.GetServices<IEventHandler<SessionEndedEvent>>().Count(handler => handler is TokenDenyListSignalHandler).Should().Be(1, "a repeated scan would run each handler twice");
        _provider.GetServices<IEventHandler<SessionEndedEvent>>().Count(handler => handler is PersonOperationSignalHandler).Should().Be(1);
    }

    [Fact]
    public async Task Broker_RegisteredFirst_ShouldKeepItsTransport_WithTheHandlersUntouched()
    {
        var broker = new BrokerTransport();
        await using var host = await StartAsync(before: services => services.AddSingleton<ISendTransport>(broker));

        using var response = await host.Client.SendAsync(Signals.Post(Signals.SessionEnded("sid-1", "person-1")));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _provider.GetRequiredService<ISendTransport>().Should().BeSameAs(broker);
        broker.Sent.Should().ContainSingle().Which.Body.Should().BeOfType<SessionEndedEvent>();
        _provider.GetServices<IEventHandler<SessionEndedEvent>>().Should().Contain(handler => handler is TokenDenyListSignalHandler);
    }

    [Fact]
    public async Task Route_ShouldBeOpen_AndOutOfApiDescriptions()
    {
        await using var host = await StartAsync();

        var route = _provider.GetRequiredService<EndpointDataSource>().Endpoints.Single(endpoint => endpoint.Metadata.GetMetadata<WebhookReceiverAttribute>() is not null);

        route.Metadata.GetMetadata<WebhookReceiverAttribute>()!.Receiver.Should().Be(IdentitySignalReceiverConstants.ReceiverName);
        route.Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();
        route.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()!.ExcludeFromDescription.Should().BeTrue();
    }

    [Fact]
    public async Task Route_ShouldBeRefusedAtTheMapping_WithNothingConfigured()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddIdentitySignalReceiver();
        await using var app = builder.Build();

        var map = () => app.MapIdentitySignals(Route);

        map.Should().Throw<InvalidOperationException>().WithMessage("*signal URL*");
    }

    [Theory]
    [InlineData("not-a-url", Issuer)]
    [InlineData("/signals/identity", Issuer)]
    [InlineData(Target, "http://id.example.test/")]
    public void Options_ShouldRefuseABadUrl_AtBoot(string audience, string issuer)
    {
        var services = new ServiceCollection();
        services.AddIdentitySignalReceiver(options =>
        {
            options.Audience = audience;
            options.TrustedIssuers.Add(new TrustedIssuer { Issuer = issuer });
        });
        using var provider = services.BuildServiceProvider();

        var read = () => provider.GetRequiredService<IOptions<IdentitySignalReceiverOptions>>().Value;

        read.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void Options_ShouldRefuseAnIssuerWithoutASignalUrl_AtBoot()
    {
        var services = new ServiceCollection();
        services.AddIdentitySignalReceiver(options => options.TrustedIssuers.Add(new TrustedIssuer { Issuer = Issuer }));
        using var provider = services.BuildServiceProvider();

        var read = () => provider.GetRequiredService<IOptions<IdentitySignalReceiverOptions>>().Value;

        read.Should().Throw<OptionsValidationException>().WithMessage("*signal URL*");
    }

    [Fact]
    public void Options_ShouldRefuseASignalUrlWithNoIssuer_UnlessAResolverIsRegistered()
    {
        var bare = new ServiceCollection().AddIdentitySignalReceiver(options => options.Audience = Target);
        var resolved = new ServiceCollection()
            .AddSingleton(new TrustedIssuerStore())
            .AddScoped<ITrustedIssuerResolver, StoreIssuerResolver>()
            .AddIdentitySignalReceiver(options => options.Audience = Target);
        using var withoutResolver = bare.BuildServiceProvider();
        using var withResolver = resolved.BuildServiceProvider();

        var readBare = () => withoutResolver.GetRequiredService<IOptions<IdentitySignalReceiverOptions>>().Value;
        var readResolved = () => withResolver.GetRequiredService<IOptions<IdentitySignalReceiverOptions>>().Value;

        readBare.Should().Throw<OptionsValidationException>().WithMessage("*ITrustedIssuerResolver*");
        readResolved.Should().NotThrow();
    }

    [Fact]
    public async Task RealSigner_ShouldPassTheReceiver_AndBeBoundToItsTarget()
    {
        await using var host = await StartAsync();
        var signal = Signals.SessionEnded("sid-1", "person-1");

        using var accepted = await host.Client.SendAsync(SignedByTheServer(signal, Target));
        using var elsewhere = await host.Client.SendAsync(SignedByTheServer(signal, "https://desk.example.test/signals/identity"));

        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted);
        elsewhere.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await SignalCapture.UntilAsync(() => !_capture.Sessions.IsEmpty, "the signal reaches the bus");
        _capture.Sessions.Single().Should().BeEquivalentTo(signal);
    }

    /// <summary>The POST the sender makes: <see cref="OidcSignalSignatureIssuer"/>'s headers over the publisher's bytes, and the dispatcher's event header.</summary>
    private HttpRequestMessage SignedByTheServer(SessionEndedEvent signal, string target)
    {
        var server = new OpenIddictServerOptions();
        server.SigningCredentials.Add(new SigningCredentials(_key, SecurityAlgorithms.RsaSha256));
        var issuer = new OidcSignalSignatureIssuer(new StaticServerOptions(server), new OidcServerOptions { Issuer = new Uri(Issuer) });
        var body = JsonSerializer.SerializeToUtf8Bytes(signal, signal.GetType());
        var headers = issuer.Issue(new WebhookSigningModel
        {
            Secret = "",
            Url = new Uri(target),
            DeliveryId = Guid.NewGuid().ToString("N"),
            EventType = nameof(SessionEndedEvent),
            Timestamp = DateTimeOffset.UtcNow,
            Payload = body,
        });

        var request = new HttpRequestMessage(HttpMethod.Post, Route) { Content = new ByteArrayContent(body) };
        request.Headers.TryAddWithoutValidation(WebhookHeaderConstants.Event, nameof(SessionEndedEvent));
        foreach (var (name, value) in headers)
            request.Headers.TryAddWithoutValidation(name, value);

        return request;
    }

    /// <summary>Sends one more signal and waits for it, so anything sent before has reached the bus, then checks only it arrived.</summary>
    private async Task AssertQuietAsync(ResourceHost host)
    {
        using var marker = await host.Client.SendAsync(Signals.Post(Signals.SessionEnded("marker", "person-1")));
        marker.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await SignalCapture.UntilAsync(() => !_capture.Sessions.IsEmpty, "the marker reaches the bus");
        _capture.Sessions.Should().ContainSingle().Which.SessionId.Should().Be("marker");
        _capture.Accesses.Should().BeEmpty();
    }

    private Task<ResourceHost> StartAsync(
        Action<IdentitySignalReceiverOptions>? configure = null,
        Action<IServiceCollection>? before = null,
        Action<IServiceCollection>? extra = null)
        => ResourceHost.StartAsync(
            services =>
            {
                services.AddAuthentication();
                before?.Invoke(services);
                services.AddIdentitySignalReceiver(options =>
                {
                    options.Audience = Target;
                    options.TrustedIssuers.Add(new TrustedIssuer { Issuer = Issuer, SigningKeys = [SignedTokens.PublicOf(_key)] });
                    configure?.Invoke(options);
                });
                extra?.Invoke(services);
                services.AddSingleton<IEventHandler<SessionEndedEvent>>(_capture);
                services.AddSingleton<IEventHandler<AccessChangedEvent>>(_capture);
            },
            endpoints =>
            {
                _provider = endpoints.ServiceProvider;
                endpoints.MapIdentitySignals(Route);
            });

    /// <summary>Hands the signer the options OpenIddict would.</summary>
    private sealed class StaticServerOptions(OpenIddictServerOptions value) : IOptionsMonitor<OpenIddictServerOptions>
    {
        public OpenIddictServerOptions CurrentValue => value;

        public OpenIddictServerOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<OpenIddictServerOptions, string?> listener) => null;
    }

    /// <summary>Stands in for a broker's send side: it keeps what would go on the wire.</summary>
    private sealed class BrokerTransport : ISendTransport
    {
        public List<EventEnvelopeModel> Sent { get; } = [];

        public ValueTask SendAsync(EventEnvelopeModel envelope, CancellationToken cancellationToken)
        {
            Sent.Add(envelope);
            return ValueTask.CompletedTask;
        }
    }
}

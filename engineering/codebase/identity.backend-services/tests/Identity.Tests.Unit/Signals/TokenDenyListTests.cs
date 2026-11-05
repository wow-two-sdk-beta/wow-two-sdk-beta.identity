using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;
using WoW.Two.Sdk.Beta.Be.Identity.Jwt;
using WoW.Two.Sdk.Beta.Be.Identity.Signals;
using WoW.Two.Sdk.Beta.Be.Identity.Testing;
using WoW.Two.Sdk.Beta.Be.Identity.Tests.Jwt;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Signals;

/// <summary>A resource server hearing signals: which bearer tokens stop counting, for how long, and which go on.</summary>
public sealed class TokenDenyListTests : IDisposable
{
    private const string Issuer = "https://id.example.test/";
    private const string OtherIssuer = "https://other.example.test/";
    private const string Files = "https://files.example.test/";
    private const string Target = "https://files.example.test/signals/identity";
    private const string Route = "/signals/identity";

    private static readonly Guid Person = Guid.Parse("01900000-0000-7000-8000-00000000000a");
    private static readonly Guid Stranger = Guid.Parse("01900000-0000-7000-8000-00000000000b");
    private static readonly Guid Organization = Guid.Parse("01900000-0000-7000-8000-000000000001");
    private static readonly Guid OtherOrganization = Guid.Parse("01900000-0000-7000-8000-000000000002");
    private static readonly Guid Session = Guid.Parse("01900000-0000-7000-8000-0000000000f1");
    private static readonly Guid OtherSession = Guid.Parse("01900000-0000-7000-8000-0000000000f2");

    private readonly FakeTimeProvider _tokenClock = new(DateTimeOffset.UtcNow);
    private readonly FakeTokenIssuer _issuer;
    private readonly FakeTokenIssuer _other = new(OtherIssuer);
    private readonly RsaSecurityKey _signalKey = SignedTokens.NewKey();
    private readonly SignalCapture _capture = new();

    public TokenDenyListTests() => _issuer = new FakeTokenIssuer(Issuer, _tokenClock);

    private SignalRequests Signals => new(_signalKey, Issuer, Target, Route);

    public void Dispose()
    {
        _issuer.Dispose();
        _other.Dispose();
    }

    [Fact]
    public async Task EndedSession_ShouldRefuseItsTokens_WhateverTheirIat_AndNoOtherSessions()
    {
        await using var host = await StartAsync();
        var revoked = _issuer.IssuePersonToken(new FakePerson { Id = Person, SessionId = Session }, Files);
        var exchanged = _issuer.IssueExchangedToken(new FakePerson { Id = Person, SessionId = Session }, "desk", Files);
        var otherSession = _issuer.IssuePersonToken(new FakePerson { Id = Person, SessionId = OtherSession }, Files);
        (await host.StatusAsync(revoked)).Should().Be(HttpStatusCode.OK);

        await SendAsync(host, Signals.SessionEnded(Session.ToString("D"), Person.ToString("D")));
        _tokenClock.Advance(TimeSpan.FromSeconds(30));
        var issuedAfterTheEnd = _issuer.IssuePersonToken(new FakePerson { Id = Person, SessionId = Session }, Files);

        (await host.StatusAsync(revoked)).Should().Be(HttpStatusCode.Unauthorized);
        (await host.StatusAsync(exchanged)).Should().Be(HttpStatusCode.Unauthorized, "an exchanged token keeps the sid");
        (await host.StatusAsync(issuedAfterTheEnd)).Should().Be(HttpStatusCode.Unauthorized, "a session's end refuses whatever it issued");
        (await host.StatusAsync(otherSession)).Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(AccessChangeKind.Changed)]
    [InlineData(AccessChangeKind.Removed)]
    public async Task ChangedAccess_ShouldRefuseTokensFromBefore_AndPassOneIssuedAfter(AccessChangeKind kind)
    {
        await using var host = await StartAsync();
        var person = new FakePerson { Id = Person, OrganizationId = Organization };
        var before = _issuer.IssuePersonToken(person, Files);
        (await host.StatusAsync(before)).Should().Be(HttpStatusCode.OK);

        _tokenClock.Advance(TimeSpan.FromSeconds(10));
        await SendAsync(host, Signals.AccessChanged(Person.ToString("D"), Organization.ToString("D"), kind, at: _tokenClock.GetUtcNow()));
        _tokenClock.Advance(TimeSpan.FromSeconds(10));
        var after = _issuer.IssuePersonToken(person, Files);

        (await host.StatusAsync(before)).Should().Be(HttpStatusCode.Unauthorized);
        (await host.StatusAsync(after)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangedAccess_ShouldPassAnotherPerson_AndAnotherOrganization_AndAnotherIssuersSameIds()
    {
        await using var host = await StartAsync();
        var person = new FakePerson { Id = Person, OrganizationId = Organization, SessionId = Session };
        var sameSessionAnotherPerson = _issuer.IssuePersonToken(person with { Id = Stranger }, Files);
        var noOrganization = _issuer.IssuePersonToken(new FakePerson { Id = Person }, Files);
        var anotherIssuer = _other.IssuePersonToken(person, Files);
        _tokenClock.Advance(TimeSpan.FromSeconds(10));

        await SendAsync(host, Signals.AccessChanged(Person.ToString("D"), Organization.ToString("D"), AccessChangeKind.Removed, at: _tokenClock.GetUtcNow()));
        await SendAsync(host, Signals.SessionEnded(Session.ToString("D"), Person.ToString("D")));

        (await host.StatusAsync(sameSessionAnotherPerson)).Should().Be(HttpStatusCode.Unauthorized, "the ended session's sid");
        var noSession = _issuer.IssuePersonToken(new FakePerson { Id = Stranger, OrganizationId = Organization }, Files);
        (await host.StatusAsync(noSession)).Should().Be(HttpStatusCode.OK, "another person");
        (await host.StatusAsync(_issuer.IssuePersonToken(new FakePerson { Id = Person, OrganizationId = OtherOrganization }, Files))).Should().Be(HttpStatusCode.OK, "another organization");
        (await host.StatusAsync(noOrganization)).Should().Be(HttpStatusCode.OK, "a token naming no organization");
        (await host.StatusAsync(anotherIssuer)).Should().Be(HttpStatusCode.OK, "the list is keyed by issuer");
    }

    [Fact]
    public async Task TokenWithNoIat_ShouldCountAsOld_ForAChange()
    {
        await using var host = await StartAsync();
        await SendAsync(host, Signals.AccessChanged(Person.ToString("D"), Organization.ToString("D"), AccessChangeKind.Changed));

        var undated = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Files,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(_signalKey, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = Person.ToString("D"),
                ["org_id"] = Organization.ToString("D"),
            },
        });

        (await host.StatusAsync(undated)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refusal_ShouldWorkWithTheFakeIssuer_AndKeepTheHostsHandler()
    {
        var heard = new List<string>();
        await using var host = await ResourceHost.StartAsync(
            services =>
            {
                services.AddJwtBearerAuthentication(options =>
                {
                    options.Audience = Files;
                    options.TrustedIssuers.Add(new TrustedIssuer { Issuer = "https://replaced.example.test/", SigningKeys = [SignedTokens.PublicOf(_signalKey)] });
                });
                services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        heard.Add(context.Principal!.FindFirst("sub")!.Value);
                        return Task.CompletedTask;
                    },
                });
                services.AddTokenDenyList();
                services.AddFakeTokenIssuer(_issuer);
                services.AddIdentitySignalReceiver(TrustSignals);
            },
            endpoints => endpoints.MapIdentitySignals(Route));
        var revoked = _issuer.IssuePersonToken(new FakePerson { Id = Person, SessionId = Session }, Files);
        var passing = _issuer.IssuePersonToken(new FakePerson { Id = Stranger, SessionId = OtherSession }, Files);

        using var response = await host.Client.SendAsync(Signals.Post(Signals.SessionEnded(Session.ToString("D"), Person.ToString("D"))));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await SignalCapture.UntilAsync(async () => await host.StatusAsync(revoked) == HttpStatusCode.Unauthorized, "the signal reaches the deny list");
        heard.Clear();

        (await host.StatusAsync(revoked)).Should().Be(HttpStatusCode.Unauthorized);
        heard.Should().BeEmpty("a refused token never reaches the host's handler");
        (await host.StatusAsync(passing)).Should().Be(HttpStatusCode.OK);
        heard.Should().Equal(Stranger.ToString("D"));
    }

    [Fact]
    public void EventsType_ShouldBeRefused_RatherThanSkipTheRefusal()
    {
        var services = new ServiceCollection();
        services.AddAuthentication().AddJwtBearer(options => options.EventsType = typeof(JwtBearerEvents));
        services.AddTokenDenyList();
        using var provider = services.BuildServiceProvider();

        var read = () => provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        read.Should().Throw<InvalidOperationException>().WithMessage("*EventsType*");
    }

    [Fact]
    public async Task HostWithoutADenyList_ShouldStillHandleSignals()
    {
        await using var host = await ResourceHost.StartAsync(
            services =>
            {
                services.AddJwtBearerAuthentication(options =>
                {
                    options.Audience = Files;
                    options.TrustedIssuers.Add(_issuer.AsTrustedIssuer());
                });
                services.AddIdentitySignalReceiver(TrustSignals);
                services.AddSingleton<Messaging.IEventHandler<SessionEndedEvent>>(_capture);
            },
            endpoints => endpoints.MapIdentitySignals(Route));
        var token = _issuer.IssuePersonToken(new FakePerson { Id = Person, SessionId = Session }, Files);

        using var response = await host.Client.SendAsync(Signals.Post(Signals.SessionEnded(Session.ToString("D"), Person.ToString("D"))));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await SignalCapture.UntilAsync(() => !_capture.Sessions.IsEmpty, "the handlers resolve with no list registered");
        (await host.StatusAsync(token)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NoSignalAtAll_ShouldLeaveATokenPassing_UntilItsOwnExpiry()
    {
        using var earlier = new FakeTokenIssuer("https://earlier.example.test/", new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-10)));
        await using var host = await StartAsync(earlier.AsTrustedIssuer());
        var live = _issuer.IssuePersonToken(new FakePerson { Id = Person, SessionId = Session, OrganizationId = Organization }, Files);
        var expired = earlier.IssuePersonToken(new FakePerson { Id = Person, SessionId = Session, OrganizationId = Organization }, Files);

        (await host.StatusAsync(live)).Should().Be(HttpStatusCode.OK, "a lost signal changes nothing here");
        (await host.StatusAsync(expired)).Should().Be(HttpStatusCode.Unauthorized, "the token's own short lifetime stays the limit");
    }

    [Fact]
    public async Task Refusal_ShouldStop_AtTokensExpireBy_ThroughThePipeline()
    {
        var hostClock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var host = await StartWithClockAsync(hostClock);
        var token = _issuer.IssuePersonToken(new FakePerson { Id = Person, SessionId = Session }, Files);

        await SendAsync(host, Signals.SessionEnded(Session.ToString("D"), Person.ToString("D"), tokensExpireBy: hostClock.GetUtcNow().AddMinutes(2)));
        (await host.StatusAsync(token)).Should().Be(HttpStatusCode.Unauthorized);

        hostClock.Advance(TimeSpan.FromMinutes(2));

        (await host.StatusAsync(token)).Should().Be(HttpStatusCode.OK, "the list forgets a signal once its tokens have expired by its own word");
    }

    [Fact]
    public async Task Entry_ShouldGo_AtTokensExpireBy()
    {
        var (list, time) = List();
        var until = time.GetUtcNow().AddMinutes(5);

        await list.AddSessionAsync(Issuer, "sid-1", until);
        await list.AddAccessAsync(Issuer, "person-1", "org-1", time.GetUtcNow(), until);

        (await list.IsDeniedAsync(Issuer, "sid-1", null, null, null)).Should().BeTrue();
        (await list.IsDeniedAsync(Issuer, null, "person-1", "org-1", time.GetUtcNow().AddMinutes(-1))).Should().BeTrue();

        time.Advance(TimeSpan.FromMinutes(5));

        (await list.IsDeniedAsync(Issuer, "sid-1", null, null, null)).Should().BeFalse();
        (await list.IsDeniedAsync(Issuer, null, "person-1", "org-1", time.GetUtcNow().AddMinutes(-10))).Should().BeFalse();
    }

    [Fact]
    public async Task Entry_ShouldGo_AfterTheDefaultLifetime_WhenASignalNamesNoEnd()
    {
        var (list, time) = List(TimeSpan.FromMinutes(30));

        await list.AddSessionAsync(Issuer, "sid-1", null);
        await list.AddAccessAsync(Issuer, "person-1", "org-1", time.GetUtcNow(), null);
        time.Advance(TimeSpan.FromMinutes(29));

        (await list.IsDeniedAsync(Issuer, "sid-1", null, null, null)).Should().BeTrue();
        (await list.IsDeniedAsync(Issuer, null, "person-1", "org-1", null)).Should().BeTrue();

        time.Advance(TimeSpan.FromMinutes(1));

        (await list.IsDeniedAsync(Issuer, "sid-1", null, null, null)).Should().BeFalse();
        (await list.IsDeniedAsync(Issuer, null, "person-1", "org-1", null)).Should().BeFalse();
    }

    [Fact]
    public async Task LateSignal_ShouldAddNothing_OnceItsTokensExpired()
    {
        var (list, time) = List();
        var past = time.GetUtcNow().AddMinutes(-1);

        await list.AddSessionAsync(Issuer, "sid-1", past);
        await list.AddAccessAsync(Issuer, "person-1", "org-1", past, past);

        (await list.IsDeniedAsync(Issuer, "sid-1", "person-1", "org-1", past.AddHours(-1))).Should().BeFalse();
    }

    [Fact]
    public async Task OutOfOrderChanges_ShouldKeepTheLaterAt_AndTheLaterEnd()
    {
        var (list, time) = List();
        var now = time.GetUtcNow();

        await list.AddAccessAsync(Issuer, "person-1", "org-1", now, now.AddMinutes(10));
        await list.AddAccessAsync(Issuer, "person-1", "org-1", now.AddMinutes(-5), now.AddMinutes(1));

        (await list.IsDeniedAsync(Issuer, null, "person-1", "org-1", now.AddSeconds(-1))).Should().BeTrue("a token from just before the later change stays refused");
        (await list.IsDeniedAsync(Issuer, null, "person-1", "org-1", now)).Should().BeFalse("issued at the change itself, it carries the new answer");
        time.Advance(TimeSpan.FromMinutes(5));
        (await list.IsDeniedAsync(Issuer, null, "person-1", "org-1", now.AddSeconds(-1))).Should().BeTrue("the earlier end doesn't shorten the entry");
    }

    [Fact]
    public async Task Ids_ShouldNotCollide_AcrossTheirParts()
    {
        var (list, time) = List();

        await list.AddAccessAsync(Issuer, "a:b", "c", time.GetUtcNow(), time.GetUtcNow().AddMinutes(5));

        (await list.IsDeniedAsync(Issuer, null, "a", "b:c", null)).Should().BeFalse();
        (await list.IsDeniedAsync(Issuer, null, "a:b", "c", null)).Should().BeTrue();
    }

    private static (ITokenDenyList List, FakeTimeProvider Time) List(TimeSpan? defaultLifetime = null)
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var options = Options.Create(new DenyListOptions { DefaultEntryLifetime = defaultLifetime ?? TimeSpan.FromHours(1) });
        return (new DistributedCacheTokenDenyList(cache, options, time), time);
    }

    private async Task SendAsync<TEvent>(ResourceHost host, TEvent signal)
        where TEvent : class
    {
        var before = _capture.Sessions.Count + _capture.Accesses.Count;
        using var response = await host.Client.SendAsync(Signals.Post(signal));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await SignalCapture.UntilAsync(() => _capture.Sessions.Count + _capture.Accesses.Count > before, "the signal reaches the handlers");
    }

    private void TrustSignals(IdentitySignalReceiverOptions options)
    {
        options.Audience = Target;
        options.TrustedIssuers.Add(new TrustedIssuer { Issuer = Issuer, SigningKeys = [SignedTokens.PublicOf(_signalKey)] });
    }

    private Task<ResourceHost> StartAsync(params TrustedIssuer[] more) => StartWithClockAsync(null, more);

    private Task<ResourceHost> StartWithClockAsync(TimeProvider? clock, params TrustedIssuer[] more)
        => ResourceHost.StartAsync(
            services =>
            {
                if (clock is not null)
                    services.AddSingleton(clock);

                services.AddJwtBearerAuthentication(options =>
                {
                    options.Audience = Files;
                    options.TrustedIssuers.Add(new TrustedIssuer { Issuer = Issuer, SigningKeys = [_issuer.AsTrustedIssuer().SigningKeys![0], SignedTokens.PublicOf(_signalKey)] });
                    options.TrustedIssuers.Add(_other.AsTrustedIssuer());
                    options.TrustedIssuers.AddRange(more);
                });
                services.AddTokenDenyList();
                services.AddIdentitySignalReceiver(TrustSignals);
                services.AddSingleton<Messaging.IEventHandler<SessionEndedEvent>>(_capture);
                services.AddSingleton<Messaging.IEventHandler<AccessChangedEvent>>(_capture);
            },
            endpoints => endpoints.MapIdentitySignals(Route));
}

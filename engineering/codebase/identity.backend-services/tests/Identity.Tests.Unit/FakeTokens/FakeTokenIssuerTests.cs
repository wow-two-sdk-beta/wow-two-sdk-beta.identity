using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Jwt;
using WoW.Two.Sdk.Beta.Be.Identity.Testing;
using WoW.Two.Sdk.Beta.Be.Identity.Tests.Jwt;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.FakeTokens;

/// <summary>Verifies the fake's token shape against the service tokens spec, and that a consumer's real host accepts it with no network.</summary>
public sealed class FakeTokenIssuerTests : IDisposable
{
    private const string Files = "https://files.example.test/";
    private const string Elsewhere = "https://elsewhere.example.test/";

    private static readonly Guid PersonId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Organization = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Session = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly FakePerson Person = new()
    {
        Id = PersonId,
        OrganizationId = Organization,
        Role = "owner",
        Permissions = ["files:share"],
        SessionId = Session,
    };

    private readonly FakeTokenIssuer _issuer = new();

    public void Dispose() => _issuer.Dispose();

    [Fact]
    public void AnyToken_ShouldHaveTheHeaderAndRegisteredClaimsOfARealOne()
    {
        var now = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        using var issuer = new FakeTokenIssuer(clock: new FakeTimeProvider(now));

        var token = issuer.IssueServiceToken("worker", Files, "files.read");

        var header = Decode(token.Split('.')[0]);
        header.GetProperty("alg").GetString().Should().Be("RS256");
        header.GetProperty("typ").GetString().Should().Be("at+jwt");
        header.GetProperty("kid").GetString().Should().NotBeNullOrEmpty();

        var payload = Decode(token.Split('.')[1]);
        payload.GetProperty("iss").GetString().Should().Be("https://fake-issuer.test/");
        payload.GetProperty("aud").ValueKind.Should().Be(JsonValueKind.String, "a service token has one audience");
        payload.GetProperty("aud").GetString().Should().Be(Files);
        payload.GetProperty("jti").GetString().Should().NotBeNullOrEmpty();
        payload.GetProperty("iat").GetInt64().Should().Be(now.ToUnixTimeSeconds());
        payload.GetProperty("nbf").GetInt64().Should().Be(now.ToUnixTimeSeconds());
        payload.GetProperty("exp").GetInt64().Should().Be(now.AddMinutes(5).ToUnixTimeSeconds());
    }

    [Fact]
    public void ServiceToken_ShouldNameTheClient_AndNoPerson()
    {
        var payload = Decode(_issuer.IssueServiceToken("worker", Files, "files.read", "files.write").Split('.')[1]);

        payload.GetProperty("sub").GetString().Should().Be("worker");
        payload.GetProperty("client_id").GetString().Should().Be("worker");
        payload.GetProperty("scope").GetString().Should().Be("files.read files.write");
        foreach (var absent in new[] { "act", "sid", "org_id", "org_role", "org_permission" })
            payload.TryGetProperty(absent, out _).Should().BeFalse(absent);
    }

    [Fact]
    public void PersonToken_ShouldCarryThePersonsClaims_WithPermissionsInAJsonArray()
    {
        var payload = Decode(_issuer.IssuePersonToken(Person, Files, scopes: "files.read").Split('.')[1]);

        payload.GetProperty("sub").GetString().Should().Be(PersonId.ToString("D"));
        payload.GetProperty("client_id").GetString().Should().Be("fake-app");
        payload.GetProperty("org_id").GetString().Should().Be(Organization.ToString("D"));
        payload.GetProperty("org_role").GetString().Should().Be($"{Organization:D}:owner");
        payload.GetProperty("org_permission").ValueKind.Should().Be(JsonValueKind.Array, "one value included");
        payload.GetProperty("org_permission").EnumerateArray().Select(value => value.GetString()).Should().Equal($"{Organization:D}:files:share");
        payload.GetProperty("sid").GetString().Should().Be(Session.ToString("D"));
        payload.TryGetProperty("act", out _).Should().BeFalse();
    }

    [Fact]
    public void PersonToken_ShouldWriteNoOrganizationClaims_WithoutAnOrganization()
    {
        var payload = Decode(_issuer.IssuePersonToken(new FakePerson { Id = PersonId }, Files).Split('.')[1]);

        foreach (var absent in new[] { "org_id", "org_role", "org_permission", "sid", "scope" })
            payload.TryGetProperty(absent, out _).Should().BeFalse(absent);
    }

    [Fact]
    public void PersonToken_ShouldRefuseARole_WithoutAnOrganization()
    {
        var issue = () => _issuer.IssuePersonToken(new FakePerson { Id = PersonId, Role = "owner" }, Files);

        issue.Should().Throw<ArgumentException>().WithMessage("*organization*");
    }

    [Fact]
    public void ExchangedToken_ShouldKeepThePerson_AndAddActAsAJsonObject()
    {
        var payload = Decode(_issuer.IssueExchangedToken(Person, "editorial", Files, "files.read").Split('.')[1]);

        payload.GetProperty("sub").GetString().Should().Be(PersonId.ToString("D"));
        payload.GetProperty("client_id").GetString().Should().Be("editorial");
        payload.GetProperty("org_role").GetString().Should().Be($"{Organization:D}:owner");
        payload.GetProperty("act").ValueKind.Should().Be(JsonValueKind.Object);
        payload.GetProperty("act").GetProperty("sub").GetString().Should().Be("editorial");
    }

    [Fact]
    public void Lifetime_ShouldDecideExp()
    {
        var now = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        using var issuer = new FakeTokenIssuer(clock: new FakeTimeProvider(now)) { Lifetime = TimeSpan.FromMinutes(1) };

        Decode(issuer.IssueServiceToken("worker", Files).Split('.')[1]).GetProperty("exp").GetInt64().Should().Be(now.AddMinutes(1).ToUnixTimeSeconds());
    }

    [Fact]
    public void Dispose_ShouldStopSigning()
    {
        var issuer = new FakeTokenIssuer();
        issuer.Dispose();

        var issue = () => issuer.IssueServiceToken("worker", Files);

        issue.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task ConsumerOnDiscovery_ShouldAcceptEachKind_OnItsRealAudience_WithNoNetwork()
    {
        using var discovery = new DiscoveryStub("https://id.example.test");
        await using var host = await ResourceHost.StartAsync(services =>
        {
            services.AddJwtBearerAuthentication(options =>
            {
                options.Issuer = "https://id.example.test/";
                options.Audience = Files;
                options.MetadataAddress = new Uri("https://id.example.test/.well-known/openid-configuration");
                options.Algorithm = SecurityAlgorithms.RsaSha256;
                options.BackchannelHttpHandler = discovery;
            });
            services.AddFakeTokenIssuer(_issuer);
        });

        await ShouldAcceptEachKindAsync(host);
        (await host.StatusAsync(_issuer.IssueServiceToken("worker", Elsewhere))).Should().Be(HttpStatusCode.Unauthorized);

        discovery.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ConsumerOnASharedKey_ShouldAcceptEachKind_ThoughItsAlgorithmIsHs256()
    {
        await using var host = await ResourceHost.StartAsync(services =>
        {
            services.AddJwtBearerAuthentication(options =>
            {
                options.Issuer = "https://id.example.test/";
                options.Audience = Files;
                options.SymmetricKey = new string('k', 32);
            });
            services.AddFakeTokenIssuer(_issuer);
        });

        await ShouldAcceptEachKindAsync(host);
        (await host.StatusAsync(_issuer.IssueServiceToken("worker", Elsewhere))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ConsumerOnTrustedIssuers_ShouldAcceptEachKind_WithNoNetwork_AndNoResolverCall()
    {
        var store = new TrustedIssuerStore();
        using var discovery = new DiscoveryStub("https://id.example.test");
        await using var host = await ResourceHost.StartAsync(services =>
        {
            services.AddSingleton(store);
            services.AddJwtBearerAuthentication(options =>
            {
                options.Audience = Files;
                options.BackchannelHttpHandler = discovery;
                options.TrustedIssuers.Add(new TrustedIssuer { Issuer = "https://id.example.test/" });
                options.UseTrustedIssuerResolver<StoreIssuerResolver>();
            });
            services.AddFakeTokenIssuer(_issuer);
        });

        await ShouldAcceptEachKindAsync(host);
        (await host.StatusAsync(_issuer.IssueServiceToken("worker", Elsewhere))).Should().Be(HttpStatusCode.Unauthorized);

        discovery.Requests.Should().BeEmpty();
        store.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task ConsumerOnListedIssuerAudiences_ShouldKeepThem()
    {
        await using var host = await ResourceHost.StartAsync(services =>
        {
            services.AddJwtBearerAuthentication(options =>
                options.TrustedIssuers.Add(new TrustedIssuer { Issuer = "https://id.example.test/", Audiences = [Files] }));
            services.AddFakeTokenIssuer(_issuer);
        });

        await ShouldAcceptEachKindAsync(host);
        (await host.StatusAsync(_issuer.IssueServiceToken("worker", Elsewhere))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AddFakeTokenIssuer_ShouldWork_WhenRegisteredBeforeTheHostsOwnAuthentication()
    {
        using var discovery = new DiscoveryStub("https://id.example.test");
        await using var host = await ResourceHost.StartAsync(services =>
        {
            services.AddFakeTokenIssuer(_issuer);
            services.AddJwtBearerAuthentication(options =>
            {
                options.Issuer = "https://id.example.test/";
                options.Audience = Files;
                options.MetadataAddress = new Uri("https://id.example.test/.well-known/openid-configuration");
                options.Algorithm = SecurityAlgorithms.RsaSha256;
                options.BackchannelHttpHandler = discovery;
            });
        });

        await ShouldAcceptEachKindAsync(host);
        discovery.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AddFakeTokenIssuer_ShouldTrustOnlyTheFake()
    {
        using var stranger = new FakeTokenIssuer();
        using var sameName = new FakeTokenIssuer("https://id.example.test/");
        await using var host = await ResourceHost.StartAsync(services =>
        {
            services.AddJwtBearerAuthentication(options =>
            {
                options.Audience = Files;
                options.TrustedIssuers.Add(sameName.AsTrustedIssuer());
            });
            services.AddFakeTokenIssuer(_issuer);
        });

        (await host.StatusAsync(_issuer.IssueServiceToken("worker", Files))).Should().Be(HttpStatusCode.OK);
        (await host.StatusAsync(stranger.IssueServiceToken("worker", Files))).Should().Be(HttpStatusCode.Unauthorized);
        (await host.StatusAsync(sameName.IssueServiceToken("worker", Files))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExpiredToken_ShouldAnswer401()
    {
        using var issuer = new FakeTokenIssuer(clock: new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(-1)));
        await using var host = await ResourceHost.StartAsync(services =>
        {
            services.AddJwtBearerAuthentication(options =>
            {
                options.Audience = Files;
                options.TrustedIssuers.Add(issuer.AsTrustedIssuer());
            });
        });

        (await host.StatusAsync(issuer.IssueServiceToken("worker", Files))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AsTrustedIssuer_ShouldRunTheRealPipeline_WithItsPartitionAndAudiences()
    {
        await using var host = await ResourceHost.StartAsync(services => services.AddJwtBearerAuthentication(options =>
            options.TrustedIssuers.Add(_issuer.AsTrustedIssuer("ocharo", Files))));

        var claims = await host.ClaimsAsync(_issuer.IssueServiceToken("worker", Files));

        claims["partition"].Should().Equal("ocharo");
        (await host.StatusAsync(_issuer.IssueServiceToken("worker", Elsewhere))).Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task ShouldAcceptEachKindAsync(ResourceHost host)
    {
        var service = await host.ClaimsAsync(_issuer.IssueServiceToken("worker", Files, "files.read"));
        service["sub"].Should().Equal("worker");
        service["client_id"].Should().Equal("worker");
        service["scope"].Should().Equal("files.read");
        service["aud"].Should().Equal(Files);

        var person = await host.ClaimsAsync(_issuer.IssuePersonToken(Person, Files, scopes: "files.read"));
        person["sub"].Should().Equal(PersonId.ToString("D"));
        person["org_permission"].Should().Equal($"{Organization:D}:files:share");
        person["sid"].Should().Equal(Session.ToString("D"));
        person["act"].Should().BeEmpty();

        var exchanged = await host.ClaimsAsync(_issuer.IssueExchangedToken(Person, "editorial", Files, "files.read"));
        exchanged["sub"].Should().Equal(PersonId.ToString("D"));
        exchanged["client_id"].Should().Equal("editorial");
        using var actor = JsonDocument.Parse(exchanged["act"].Single());
        actor.RootElement.GetProperty("sub").GetString().Should().Be("editorial");
    }

    private static JsonElement Decode(string part)
    {
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(part)));
        return document.RootElement.Clone();
    }
}

using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Jwt;
using WoW.Two.Sdk.Beta.Be.Identity.Testing;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Jwt;

/// <summary>Drives trusted-issuer mode through a real bearer pipeline: which token passes, and for whom.</summary>
public sealed class TrustedIssuerAuthenticationTests : IDisposable
{
    private const string Files = "https://files.example.test/";
    private const string Desk = "https://desk.example.test/";
    private const string IssuerA = "https://a.example.test/";
    private const string IssuerB = "https://b.example.test/";

    private readonly FakeTokenIssuer _a = new(IssuerA);
    private readonly FakeTokenIssuer _b = new(IssuerB);

    public void Dispose()
    {
        _a.Dispose();
        _b.Dispose();
    }

    [Fact]
    public async Task Token_ShouldPass_WhenItNamesAnAudienceOfTheHost_AndAnswer401ForAnother()
    {
        await using var host = await StartAsync(options =>
        {
            options.Audience = Files;
            options.Audiences.Add(Desk);
            options.TrustedIssuers.Add(_a.AsTrustedIssuer());
        });

        (await host.StatusAsync(_a.IssueServiceToken("worker", Files))).Should().Be(HttpStatusCode.OK);
        (await host.StatusAsync(_a.IssueServiceToken("worker", Desk))).Should().Be(HttpStatusCode.OK);
        (await host.StatusAsync(_a.IssueServiceToken("worker", "https://other.example.test/"))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IssuerAudiences_ShouldReplaceTheSharedOnes_ForThatIssuerAlone()
    {
        await using var host = await StartAsync(options =>
        {
            options.Audience = Files;
            options.TrustedIssuers.Add(_a.AsTrustedIssuer(null, Desk));
            options.TrustedIssuers.Add(_b.AsTrustedIssuer());
        });

        (await host.StatusAsync(_a.IssueServiceToken("worker", Desk))).Should().Be(HttpStatusCode.OK);
        (await host.StatusAsync(_a.IssueServiceToken("worker", Files))).Should().Be(HttpStatusCode.Unauthorized);
        (await host.StatusAsync(_b.IssueServiceToken("worker", Files))).Should().Be(HttpStatusCode.OK);
        (await host.StatusAsync(_b.IssueServiceToken("worker", Desk))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_ShouldAnswer401_WhenItsIssuerIsUnknown_WithoutReachingANetwork()
    {
        using var discovery = new DiscoveryStub("https://stranger.example.test");
        using var stranger = new FakeTokenIssuer("https://stranger.example.test/");
        await using var host = await StartAsync(options =>
        {
            options.Audience = Files;
            options.BackchannelHttpHandler = discovery;
            options.TrustedIssuers.Add(_a.AsTrustedIssuer());
        });

        (await host.StatusAsync(stranger.IssueServiceToken("worker", Files))).Should().Be(HttpStatusCode.Unauthorized);

        discovery.Requests.Should().BeEmpty("a token's iss is not an address to fetch");
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("a.b.c")]
    [InlineData("eyJhbGciOiJSUzI1NiJ9.eyJpc3MiOjV9.c2ln")]
    public async Task Token_ShouldAnswer401_WhenItCannotBeRead(string token)
    {
        await using var host = await StartAsync(options =>
        {
            options.Audience = Files;
            options.TrustedIssuers.Add(_a.AsTrustedIssuer());
        });

        (await host.StatusAsync(token)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Resolver_ShouldNotBeAsked_ForAListedIssuer()
    {
        var store = new TrustedIssuerStore();
        await using var host = await StartAsync(options =>
        {
            options.Audience = Files;
            options.TrustedIssuers.Add(_a.AsTrustedIssuer());
            options.UseTrustedIssuerResolver<StoreIssuerResolver>();
        }, store);

        (await host.StatusAsync(_a.IssueServiceToken("worker", Files))).Should().Be(HttpStatusCode.OK);

        store.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task Resolver_ShouldFindANewIssuer_WithNoRestart()
    {
        var store = new TrustedIssuerStore();
        await using var host = await StartAsync(options =>
        {
            options.Audience = Files;
            options.TrustedIssuers.Add(_a.AsTrustedIssuer());
            options.UseTrustedIssuerResolver<StoreIssuerResolver>();
        }, store);
        var token = _b.IssueServiceToken("worker", Files);

        (await host.StatusAsync(token)).Should().Be(HttpStatusCode.Unauthorized);
        store.Asked.Should().Equal(IssuerB);

        store.Issuers[IssuerB] = _b.AsTrustedIssuer("tenant-b");

        (await host.ClaimsAsync(token))["partition"].Should().Equal("tenant-b");
    }

    [Fact]
    public async Task Resolver_ShouldWorkAlone_WithTheAudiencesOfTheOptions()
    {
        var store = new TrustedIssuerStore();
        store.Issuers[IssuerA] = _a.AsTrustedIssuer();
        await using var host = await StartAsync(options =>
        {
            options.Audiences.Add(Files);
            options.UseTrustedIssuerResolver<StoreIssuerResolver>();
        }, store);

        (await host.StatusAsync(_a.IssueServiceToken("worker", Files))).Should().Be(HttpStatusCode.OK);
        (await host.StatusAsync(_a.IssueServiceToken("worker", Desk))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Resolver_ShouldAnswer401_WhenItNamesAnotherIssuer_OrFails()
    {
        var store = new TrustedIssuerStore();
        store.Issuers[IssuerB] = _a.AsTrustedIssuer();
        await using var host = await StartAsync(options =>
        {
            options.Audience = Files;
            options.UseTrustedIssuerResolver<StoreIssuerResolver>();
        }, store);

        (await host.StatusAsync(_b.IssueServiceToken("worker", Files))).Should().Be(HttpStatusCode.Unauthorized);

        store.Failure = new InvalidOperationException("The store is down.");
        (await host.StatusAsync(_b.IssueServiceToken("worker", Files))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ResolvedIssuer_ShouldAnswer401_WhenNoAudienceAppliesToIt()
    {
        var store = new TrustedIssuerStore();
        store.Issuers[IssuerB] = _b.AsTrustedIssuer();
        await using var host = await StartAsync(options =>
        {
            options.TrustedIssuers.Add(_a.AsTrustedIssuer(null, Files));
            options.UseTrustedIssuerResolver<StoreIssuerResolver>();
        }, store);

        (await host.StatusAsync(_a.IssueServiceToken("worker", Files))).Should().Be(HttpStatusCode.OK);
        (await host.StatusAsync(_b.IssueServiceToken("worker", Files))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IssuerAKey_ShouldNotSignForIssuerB()
    {
        var keyOfA = SignedTokens.NewKey();
        await using var host = await StartAsync(options =>
        {
            options.Audience = Files;
            options.TrustedIssuers.Add(new TrustedIssuer { Issuer = IssuerA, SigningKeys = [SignedTokens.PublicOf(keyOfA)] });
            options.TrustedIssuers.Add(_b.AsTrustedIssuer());
        });

        (await host.StatusAsync(SignedTokens.Sign(keyOfA, IssuerA, Files))).Should().Be(HttpStatusCode.OK);
        (await host.StatusAsync(SignedTokens.Sign(keyOfA, IssuerB, Files))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IssuerAKey_ShouldNotSignForIssuerB_WhenBothPublishTheSameKid()
    {
        const string SharedKid = "shared";
        var keyOfA = SignedTokens.NewKey(SharedKid);
        var keyOfB = SignedTokens.NewKey(SharedKid);
        await using var host = await StartAsync(options =>
        {
            options.Audience = Files;
            options.TrustedIssuers.Add(new TrustedIssuer { Issuer = IssuerA, SigningKeys = [SignedTokens.PublicOf(keyOfA)] });
            options.TrustedIssuers.Add(new TrustedIssuer { Issuer = IssuerB, SigningKeys = [SignedTokens.PublicOf(keyOfB)] });
        });

        (await host.StatusAsync(SignedTokens.Sign(keyOfB, IssuerB, Files))).Should().Be(HttpStatusCode.OK);
        (await host.StatusAsync(SignedTokens.Sign(keyOfA, IssuerB, Files))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Partition_ShouldBeTheIssuers_WhateverTheTokenSays()
    {
        var key = SignedTokens.NewKey();
        await using var host = await StartAsync(options =>
        {
            options.Audience = Files;
            options.TrustedIssuers.Add(new TrustedIssuer { Issuer = IssuerA, SigningKeys = [SignedTokens.PublicOf(key)], Partition = "ocharo" });
        });

        var claims = await host.ClaimsAsync(SignedTokens.Sign(key, IssuerA, Files, new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = "caller",
            ["partition"] = new[] { "victim", "other" },
            ["Partition"] = "victim",
        }));

        claims["partition"].Should().Equal("ocharo");
        claims["Partition"].Should().BeEmpty();
    }

    [Fact]
    public async Task Partition_ShouldBeStripped_WhenTheIssuerStampsNone()
    {
        var key = SignedTokens.NewKey();
        await using var host = await StartAsync(options =>
        {
            options.Audience = Files;
            options.TrustedIssuers.Add(new TrustedIssuer { Issuer = IssuerA, SigningKeys = [SignedTokens.PublicOf(key)] });
        });

        var claims = await host.ClaimsAsync(SignedTokens.Sign(key, IssuerA, Files, new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = "caller",
            ["partition"] = "victim",
        }));

        claims["partition"].Should().BeEmpty();
        claims["sub"].Should().Equal("caller");
    }

    [Fact]
    public async Task Hs256_ShouldBeRefused_WhenSignedWithTheIssuersPublicKey()
    {
        var key = SignedTokens.NewKey();
        var publicKey = SignedTokens.PublicOf(key);
        await using var host = await StartAsync(options =>
        {
            options.Audience = Files;
            options.TrustedIssuers.Add(new TrustedIssuer { Issuer = IssuerA, SigningKeys = [publicKey] });
        });
        var confused = new SymmetricSecurityKey(publicKey.Parameters.Modulus) { KeyId = publicKey.KeyId };

        (await host.StatusAsync(SignedTokens.Sign(key, IssuerA, Files))).Should().Be(HttpStatusCode.OK);
        (await host.StatusAsync(SignedTokens.Sign(confused, IssuerA, Files, algorithm: SecurityAlgorithms.HmacSha256)))
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Hs256_ShouldBeRefused_EvenWithASharedKeyFromTheResolver()
    {
        var shared = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 32)));
        var store = new TrustedIssuerStore();
        store.Issuers[IssuerA] = new TrustedIssuer { Issuer = IssuerA, SigningKeys = [shared] };
        await using var host = await StartAsync(options =>
        {
            options.Audience = Files;
            options.UseTrustedIssuerResolver<StoreIssuerResolver>();
        }, store);

        (await host.StatusAsync(SignedTokens.Sign(shared, IssuerA, Files, algorithm: SecurityAlgorithms.HmacSha256)))
            .Should().Be(HttpStatusCode.Unauthorized);
        store.Asked.Should().Equal(IssuerA);
    }

    [Fact]
    public async Task Token_ShouldAnswer401_WhenExpired()
    {
        _a.Lifetime = TimeSpan.FromMinutes(-5);
        await using var host = await StartAsync(options =>
        {
            options.Audience = Files;
            options.TrustedIssuers.Add(_a.AsTrustedIssuer());
        });

        (await host.StatusAsync(_a.IssueServiceToken("worker", Files))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SingleIssuer_ShouldAcceptAudiencesBesideItsAudience()
    {
        var secret = new string('k', 32);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        await using var host = await ResourceHost.StartAsync(services => services.AddJwtBearerAuthentication(options =>
        {
            options.Issuer = IssuerA;
            options.Audience = Files;
            options.Audiences.Add(Desk);
            options.SymmetricKey = secret;
        }));

        (await host.StatusAsync(SignedTokens.Sign(key, IssuerA, Files, algorithm: SecurityAlgorithms.HmacSha256))).Should().Be(HttpStatusCode.OK);
        (await host.StatusAsync(SignedTokens.Sign(key, IssuerA, Desk, algorithm: SecurityAlgorithms.HmacSha256))).Should().Be(HttpStatusCode.OK);
        (await host.StatusAsync(SignedTokens.Sign(key, IssuerA, "https://other.example.test/", algorithm: SecurityAlgorithms.HmacSha256)))
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    private static Task<ResourceHost> StartAsync(Action<JwtOptions> configure, TrustedIssuerStore? store = null) =>
        ResourceHost.StartAsync(services =>
        {
            services.AddSingleton(store ?? new TrustedIssuerStore());
            services.AddJwtBearerAuthentication(configure);
        });
}

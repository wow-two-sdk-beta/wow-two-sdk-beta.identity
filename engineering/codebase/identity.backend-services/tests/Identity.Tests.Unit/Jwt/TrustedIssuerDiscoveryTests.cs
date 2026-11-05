using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Jwt;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Jwt;

/// <summary>Drives discovery through <see cref="JwtOptions.BackchannelHttpHandler"/>: when an issuer's documents are fetched, and when again.</summary>
public sealed class TrustedIssuerDiscoveryTests
{
    private const string Authority = "https://issuer.example.test";
    private const string Issuer = Authority + "/";
    private const string Files = "https://files.example.test/";

    [Fact]
    public async Task Keys_ShouldComeFromTheWellKnownAddress_AndBeCached()
    {
        var key = SignedTokens.NewKey();
        using var discovery = new DiscoveryStub(Authority);
        discovery.Publish(key);
        await using var host = await StartAsync(discovery);

        (await host.StatusAsync(SignedTokens.Sign(key, Issuer, Files))).Should().Be(HttpStatusCode.OK);
        (await host.StatusAsync(SignedTokens.Sign(key, Issuer, Files))).Should().Be(HttpStatusCode.OK);

        discovery.MetadataFetches.Should().Be(1);
        discovery.Requests.First().Should().Be(new Uri(Authority + "/.well-known/openid-configuration"));
    }

    [Fact]
    public async Task Keys_ShouldComeFromTheConfiguredAddress_WhenTheIssuerSetsOne()
    {
        var key = SignedTokens.NewKey();
        using var discovery = new DiscoveryStub(Authority);
        discovery.Publish(key);
        var address = new Uri("https://elsewhere.example.test/.well-known/openid-configuration");
        await using var host = await ResourceHost.StartAsync(services => services.AddJwtBearerAuthentication(options =>
        {
            options.Audience = Files;
            options.BackchannelHttpHandler = discovery;
            options.TrustedIssuers.Add(new TrustedIssuer { Issuer = Issuer, MetadataAddress = address });
        }));

        (await host.StatusAsync(SignedTokens.Sign(key, Issuer, Files))).Should().Be(HttpStatusCode.OK);

        discovery.Requests.First().Should().Be(address);
    }

    [Fact]
    public async Task UnknownKid_ShouldRefreshOnce_ThenPass()
    {
        var first = SignedTokens.NewKey();
        var rotated = SignedTokens.NewKey();
        using var discovery = new DiscoveryStub(Authority);
        discovery.Publish(first);
        await using var host = await StartAsync(discovery);
        (await host.StatusAsync(SignedTokens.Sign(first, Issuer, Files))).Should().Be(HttpStatusCode.OK);

        discovery.Publish(rotated);

        // The very first request after the rotation: the refresh it asks for must reach its own retry.
        (await host.StatusAsync(SignedTokens.Sign(rotated, Issuer, Files))).Should().Be(HttpStatusCode.OK);
        discovery.MetadataFetches.Should().Be(2);

        (await host.StatusAsync(SignedTokens.Sign(rotated, Issuer, Files))).Should().Be(HttpStatusCode.OK);
        discovery.MetadataFetches.Should().Be(2, "the new keys are held now");
    }

    [Fact]
    public async Task UnknownKid_ShouldFetchNoMore_WhileARefreshIsRecent()
    {
        var known = SignedTokens.NewKey();
        var forged = SignedTokens.NewKey();
        using var discovery = new DiscoveryStub(Authority);
        discovery.Publish(known);
        await using var host = await StartAsync(discovery);
        (await host.StatusAsync(SignedTokens.Sign(known, Issuer, Files))).Should().Be(HttpStatusCode.OK);

        (await host.StatusAsync(SignedTokens.Sign(forged, Issuer, Files))).Should().Be(HttpStatusCode.Unauthorized);
        discovery.MetadataFetches.Should().Be(2);

        var started = DateTime.UtcNow;
        (await host.StatusAsync(SignedTokens.Sign(forged, Issuer, Files))).Should().Be(HttpStatusCode.Unauthorized);
        (await host.StatusAsync(SignedTokens.Sign(forged, Issuer, Files))).Should().Be(HttpStatusCode.Unauthorized);

        discovery.MetadataFetches.Should().Be(2);
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(4), "a forged kid must not hold a request for the refresh wait");
    }

    [Fact]
    public async Task UnknownKid_ShouldShareOneRefresh_AcrossConcurrentRequests()
    {
        var first = SignedTokens.NewKey();
        var rotated = SignedTokens.NewKey();
        using var discovery = new DiscoveryStub(Authority);
        discovery.Publish(first);
        await using var host = await StartAsync(discovery);
        (await host.StatusAsync(SignedTokens.Sign(first, Issuer, Files))).Should().Be(HttpStatusCode.OK);

        discovery.KeySetDelay = TimeSpan.FromMilliseconds(200);
        discovery.Publish(rotated);
        var token = SignedTokens.Sign(rotated, Issuer, Files);

        var statuses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => host.StatusAsync(token)));

        statuses.Should().AllBeEquivalentTo(HttpStatusCode.OK);
        discovery.MetadataFetches.Should().Be(2);
    }

    [Fact]
    public async Task WrongSignature_ShouldNotRefresh_WhenItsKidIsKnown()
    {
        const string Kid = "known";
        var published = SignedTokens.NewKey(Kid);
        var impostor = SignedTokens.NewKey(Kid);
        using var discovery = new DiscoveryStub(Authority);
        discovery.Publish(published);
        await using var host = await StartAsync(discovery);

        (await host.StatusAsync(SignedTokens.Sign(impostor, Issuer, Files))).Should().Be(HttpStatusCode.Unauthorized);

        discovery.MetadataFetches.Should().Be(1);
    }

    [Fact]
    public async Task StaticKeys_ShouldNeverFetch()
    {
        var key = SignedTokens.NewKey();
        var other = SignedTokens.NewKey();
        using var discovery = new DiscoveryStub(Authority);
        await using var host = await ResourceHost.StartAsync(services => services.AddJwtBearerAuthentication(options =>
        {
            options.Audience = Files;
            options.BackchannelHttpHandler = discovery;
            options.TrustedIssuers.Add(new TrustedIssuer { Issuer = Issuer, SigningKeys = [SignedTokens.PublicOf(key)] });
        }));

        (await host.StatusAsync(SignedTokens.Sign(key, Issuer, Files))).Should().Be(HttpStatusCode.OK);
        (await host.StatusAsync(SignedTokens.Sign(other, Issuer, Files))).Should().Be(HttpStatusCode.Unauthorized);

        discovery.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task UnreachableMetadata_ShouldAnswer401()
    {
        using var discovery = new DiscoveryStub(Authority) { IsDown = true };
        await using var host = await StartAsync(discovery);

        (await host.StatusAsync(SignedTokens.Sign(SignedTokens.NewKey(), Issuer, Files))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FailedRefresh_ShouldAnswer401_WithinItsWait_AndKeepTheKeysItHolds()
    {
        var known = SignedTokens.NewKey();
        using var discovery = new DiscoveryStub(Authority);
        discovery.Publish(known);
        await using var host = await StartAsync(discovery);
        (await host.StatusAsync(SignedTokens.Sign(known, Issuer, Files))).Should().Be(HttpStatusCode.OK);

        discovery.IsDown = true;
        var started = DateTime.UtcNow;

        (await host.StatusAsync(SignedTokens.Sign(SignedTokens.NewKey(), Issuer, Files))).Should().Be(HttpStatusCode.Unauthorized);

        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(30));
        (await host.StatusAsync(SignedTokens.Sign(known, Issuer, Files))).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SingleIssuer_ShouldFetchThroughTheBackchannelHandler()
    {
        var key = SignedTokens.NewKey();
        using var discovery = new DiscoveryStub(Authority);
        discovery.Publish(key);
        await using var host = await ResourceHost.StartAsync(services => services.AddJwtBearerAuthentication(options =>
        {
            options.Issuer = Issuer;
            options.Audience = Files;
            options.Algorithm = SecurityAlgorithms.RsaSha256;
            options.MetadataAddress = new Uri(Authority + "/.well-known/openid-configuration");
            options.BackchannelHttpHandler = discovery;
        }));

        (await host.StatusAsync(SignedTokens.Sign(key, Issuer, Files))).Should().Be(HttpStatusCode.OK);

        discovery.MetadataFetches.Should().Be(1);
    }

    [Fact]
    public async Task HttpMetadata_ShouldBeFetched_WithTheDevelopmentEscape()
    {
        const string LocalIssuer = "http://localhost:8080/";
        var key = SignedTokens.NewKey();
        using var discovery = new DiscoveryStub("http://localhost:8080");
        discovery.Publish(key);
        await using var host = await ResourceHost.StartAsync(services => services.AddJwtBearerAuthentication(options =>
        {
            options.Audience = Files;
            options.BackchannelHttpHandler = discovery;
            options.IsInsecureMetadataAllowedInDevelopment = true;
            options.TrustedIssuers.Add(new TrustedIssuer { Issuer = LocalIssuer });
        }));

        (await host.StatusAsync(SignedTokens.Sign(key, LocalIssuer, Files))).Should().Be(HttpStatusCode.OK);
    }

    private static Task<ResourceHost> StartAsync(DiscoveryStub discovery) =>
        ResourceHost.StartAsync(services => services.AddJwtBearerAuthentication(options =>
        {
            options.Audience = Files;
            options.BackchannelHttpHandler = discovery;
            options.TrustedIssuers.Add(new TrustedIssuer { Issuer = Issuer });
        }));
}

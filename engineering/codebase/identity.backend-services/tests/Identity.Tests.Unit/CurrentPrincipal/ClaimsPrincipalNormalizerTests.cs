using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;
using WoW.Two.Sdk.Beta.Be.Identity.CurrentPrincipal;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.CurrentPrincipal;

/// <summary>One reader over every scheme: the same caller shape from a JWT, a cookie session and an API key.</summary>
public sealed class ClaimsPrincipalNormalizerTests
{
    private const string Organization = "0193a7d0-0000-7000-8000-000000000001";

    private static ClaimsPrincipal Authenticated(string scheme, params Claim[] claims) => new(new ClaimsIdentity(claims, scheme));

    [Fact]
    public void Anonymous_ShouldHaveNoSubject()
    {
        var caller = ClaimsPrincipalNormalizer.Normalize(new ClaimsPrincipal(new ClaimsIdentity()));

        caller.Kind.Should().Be(PrincipalKind.Anonymous);
        caller.Subject.Should().BeNull();
        caller.Scopes.Should().BeEmpty();
    }

    [Fact]
    public void ServiceToken_ShouldBeAService_WithItsSpaceDelimitedScopes()
    {
        var caller = ClaimsPrincipalNormalizer.Normalize(Authenticated(
            "Bearer",
            new Claim("iss", "https://id.example.test/"),
            new Claim("sub", "worker"),
            new Claim("client_id", "worker"),
            new Claim("scope", "desk.media desk.orders")));

        caller.Should().BeEquivalentTo(new
        {
            Kind = PrincipalKind.Service,
            Subject = "worker",
            ClientId = "worker",
            Issuer = "https://id.example.test/",
        });
        caller.Scopes.Should().BeEquivalentTo("desk.media", "desk.orders");
        caller.Actor.Should().BeNull();
    }

    [Fact]
    public void AzpFromAnotherIssuer_ShouldNameTheClientToo()
    {
        var caller = ClaimsPrincipalNormalizer.Normalize(Authenticated("Bearer", new Claim("sub", "worker"), new Claim("azp", "worker"), new Claim("scp", "a.read")));

        caller.Kind.Should().Be(PrincipalKind.Service);
        caller.Scopes.Should().Equal("a.read");
    }

    [Fact]
    public void PersonToken_ShouldStripTheOrganizationFromRolesAndPermissions_AndDropOtherOrganizations()
    {
        var caller = ClaimsPrincipalNormalizer.Normalize(Authenticated(
            "Bearer",
            new Claim("sub", "person-1"),
            new Claim("client_id", "editorial"),
            new Claim("org_id", Organization),
            new Claim("org_role", $"{Organization}:owner"),
            new Claim("org_role", "another:viewer"),
            new Claim("org_permission", $"{Organization}:desk:orders:edit"),
            new Claim("partition", "ocharo")));

        caller.Kind.Should().Be(PrincipalKind.Person);
        caller.OrganizationId.Should().Be(Organization);
        caller.Roles.Should().Equal("owner");
        caller.Permissions.Should().Equal("desk:orders:edit");
        caller.Partition.Should().Be("ocharo");
    }

    [Fact]
    public void ExchangedToken_ShouldReadItsActorChain()
    {
        var caller = ClaimsPrincipalNormalizer.Normalize(Authenticated(
            "Bearer",
            new Claim("sub", "person-1"),
            new Claim("client_id", "editorial"),
            new Claim("act", """{ "sub": "editorial", "act": { "sub": "gateway" } }""", JsonClaimValueTypes.Json)));

        caller.Kind.Should().Be(PrincipalKind.Person);
        caller.Actor.Should().BeEquivalentTo(new ActorInfo { Subject = "editorial", Actor = new ActorInfo { Subject = "gateway" } });
    }

    [Fact]
    public void UnreadableActor_ShouldStillSaySomeoneActs()
        => ClaimsPrincipalNormalizer.Normalize(Authenticated("Bearer", new Claim("sub", "p"), new Claim("act", "not json"))).Actor!.Subject.Should().Be("not json");

    [Fact]
    public void CookieSession_ShouldNameThePersonByItsSubjectClaim_AndKeepEveryMembershipWithNoActiveOrganization()
    {
        var caller = ClaimsPrincipalNormalizer.Normalize(Authenticated(
            "Identity.Application",
            new Claim(ClaimTypes.NameIdentifier, "person-1"),
            new Claim("org_role", $"{Organization}:owner"),
            new Claim("org_role", "another:viewer")));

        caller.Kind.Should().Be(PrincipalKind.Person);
        caller.Subject.Should().Be("person-1");
        caller.Issuer.Should().BeNull();
        caller.Roles.Should().BeEquivalentTo($"{Organization}:owner", "another:viewer");
    }

    [Fact]
    public void ApiKey_ShouldBeAKey_WithPerClaimScopes()
    {
        var caller = ClaimsPrincipalNormalizer.Normalize(Authenticated(
            ApiKeyAuthenticationDefaults.Scheme,
            new Claim(ApiKeyAuthenticationDefaults.KeyIdClaim, "key-1"),
            new Claim(ApiKeyAuthenticationDefaults.ScopeClaim, "files.read"),
            new Claim(ApiKeyAuthenticationDefaults.ScopeClaim, "files.write"),
            new Claim("partition", "tenant-a")));

        caller.Kind.Should().Be(PrincipalKind.ApiKey);
        caller.Subject.Should().Be("key-1");
        caller.Scopes.Should().BeEquivalentTo("files.read", "files.write");
        caller.Partition.Should().Be("tenant-a");
    }
}

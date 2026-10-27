using System.Security.Claims;
using AwesomeAssertions;
using OpenIddict.Abstractions;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.OidcServer;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Server;

// The Providers package's Identity.Claims namespace would shadow OpenIddict's claim names here.
using Claims = OpenIddict.Abstractions.OpenIddictConstants.Claims;

/// <summary>The one claim mapping: the person's claims, one active organization, and where each claim goes.</summary>
public sealed class OidcClaimMapperTests
{
    private const string StampType = "AspNet.Identity.SecurityStamp";
    private static readonly OidcClientProfile Client = new("wheelhouse", "wheelhouse-api", PermissionNamespace: null);
    private static readonly Guid First = Guid.Parse("01900000-0000-7000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("01900000-0000-7000-8000-000000000002");

    private static readonly Dictionary<string, IReadOnlyList<string>> Permissions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["owner"] = ["wheelhouse:deploy", "wheelhouse:settings", "logs:read"],
        ["viewer"] = ["wheelhouse:read"],
    };

    [Fact]
    public void PersonClaims_ShouldCarryTheUserIdNameEmailAndStamp_ReplacingOlderValues()
    {
        var user = new IdentityUser { Id = Guid.CreateVersion7(), UserName = "alice", Email = "alice@example.test", EmailConfirmed = true, SecurityStamp = "stamp-2" };
        var identity = OidcClaimMapper.CreateIdentity(Client, [Scopes.OpenId], [AuthenticationMethodConstants.Password], DateTimeOffset.UnixEpoch);
        identity.AddClaim(new Claim(Claims.Name, "old-name"));
        identity.AddClaim(new Claim(StampType, "stamp-1"));

        OidcClaimMapper.SetPersonClaims(identity, user, StampType);

        identity.GetClaim(Claims.Subject).Should().Be(user.Id.ToString("D"));
        identity.GetClaims(Claims.Name).Should().Equal("alice");
        identity.GetClaim(Claims.Email).Should().Be("alice@example.test");
        identity.FindFirst(Claims.EmailVerified)!.ValueType.Should().Be(ClaimValueTypes.Boolean, "a token carries it as a JSON boolean");
        bool.Parse(identity.GetClaim(Claims.EmailVerified)!).Should().BeTrue();
        identity.GetClaims(StampType).Should().Equal("stamp-2");
        OidcClaimMapper.ReadValues(identity, Claims.AuthenticationMethodReference).Should().Equal(AuthenticationMethodConstants.Password);
        identity.FindFirst(Claims.AuthenticationMethodReference)!.ValueType.Should().Be("JSON_ARRAY", "OIDC defines amr as an array");
        identity.GetAudiences().Should().Equal("wheelhouse-api");
    }

    [Fact]
    public void OrganizationClaims_ShouldTakeTheOnlyMembership()
    {
        var identity = new ClaimsIdentity();

        var active = OidcClaimMapper.SetOrganizationClaims(identity, [Membership(Second, "viewer", day: 2)], Permissions.GetValueOrDefault!, permissionNamespace: null, preferred: null);

        active.Should().Be(Second);
        identity.GetClaim(OrganizationClaimTypeConstants.Id).Should().Be(Second.ToString());
        identity.GetClaim(OrganizationClaimTypeConstants.Role).Should().Be($"{Second}:viewer");
        OidcClaimMapper.ReadValues(identity, OrganizationClaimTypeConstants.Permission).Should().Equal($"{Second}:wheelhouse:read");
    }

    [Fact]
    public void OrganizationClaims_ShouldTakeTheEarliestMembership_UnlessTheTokenAlreadyCarriesOne()
    {
        IReadOnlyList<OrganizationMembershipModel> memberships = [Membership(Second, "viewer", day: 2), Membership(First, "owner", day: 1)];

        OidcClaimMapper.SetOrganizationClaims(new ClaimsIdentity(), memberships, Permissions.GetValueOrDefault!, null, preferred: null).Should().Be(First);
        OidcClaimMapper.SetOrganizationClaims(new ClaimsIdentity(), memberships, Permissions.GetValueOrDefault!, null, preferred: Second).Should().Be(Second);
        OidcClaimMapper.SetOrganizationClaims(new ClaimsIdentity(), memberships, Permissions.GetValueOrDefault!, null, preferred: Guid.CreateVersion7())
            .Should().Be(First, "a membership that ended falls back to the earliest");
    }

    [Fact]
    public void OrganizationClaims_ShouldKeepOnlyTheClientsPermissionNamespace()
    {
        var identity = new ClaimsIdentity();

        OidcClaimMapper.SetOrganizationClaims(identity, [Membership(First, "owner", day: 1)], Permissions.GetValueOrDefault!, "wheelhouse", preferred: null);

        OidcClaimMapper.ReadValues(identity, OrganizationClaimTypeConstants.Permission).Should().BeEquivalentTo($"{First}:wheelhouse:deploy", $"{First}:wheelhouse:settings");
    }

    [Fact]
    public void OrganizationClaims_ShouldLeaveAPersonWithoutMembershipsWithout()
    {
        var identity = new ClaimsIdentity([new Claim(OrganizationClaimTypeConstants.Id, First.ToString())]);

        OidcClaimMapper.SetOrganizationClaims(identity, [], Permissions.GetValueOrDefault!, null, preferred: First).Should().BeNull();

        identity.Claims.Should().BeEmpty("a removed membership drops the organization claims at the next refresh");
    }

    [Fact]
    public void Destinations_ShouldFollowScopes_AndKeepTheStampInside()
    {
        var withScopes = Identity(Scopes.OpenId, Scopes.Profile, Scopes.Email);
        var bare = Identity(Scopes.OpenId);

        Destinations(withScopes, Claims.Name).Should().BeEquivalentTo(Both);
        Destinations(withScopes, Claims.Email).Should().BeEquivalentTo(Both);
        Destinations(bare, Claims.Name).Should().BeEmpty();
        Destinations(bare, Claims.EmailVerified).Should().BeEmpty();
        Destinations(bare, Claims.Subject).Should().BeEquivalentTo(Both);
        Destinations(bare, Claims.AuthenticationMethodReference).Should().BeEquivalentTo(Both);
        Destinations(bare, OrganizationClaimTypeConstants.Permission).Should().BeEquivalentTo(Both);
        Destinations(bare, Claims.AuthenticationTime).Should().Equal(OpenIddictConstants.Destinations.IdentityToken);
        Destinations(bare, StampType).Should().BeEmpty();
        Destinations(bare, SessionClaimTypeConstants.SessionId).Should().BeEquivalentTo(Both);
    }

    [Fact]
    public void Session_ShouldTravelAsSid_InBothTokens_ButStayOutOfUserInfo()
    {
        var session = Guid.Parse("01900000-0000-7000-8000-0000000000aa");
        var identity = OidcClaimMapper.CreateIdentity(Client, [Scopes.OpenId], [AuthenticationMethodConstants.Password], authenticatedAt: null, session);
        identity.SetClaim(Claims.Subject, "person");

        identity.GetClaim(SessionClaimTypeConstants.SessionId).Should().Be(session.ToString("D"));
        OidcClaimMapper.GetDestinations(identity.FindFirst(SessionClaimTypeConstants.SessionId)!).Should().BeEquivalentTo(Both);
        OidcClaimMapper.ToUserInfo(new ClaimsPrincipal(identity)).Keys.Should().Equal(Claims.Subject);
        OidcClaimMapper.CreateIdentity(Client, [Scopes.OpenId], [], authenticatedAt: null).HasClaim(c => c.Type == SessionClaimTypeConstants.SessionId)
            .Should().BeFalse("a grant without a session carries no sid");
    }

    [Fact]
    public void UserInfo_ShouldAnswerTheProfileAndOrganizationClaims_WithTypedValues()
    {
        var identity = Identity(Scopes.OpenId, Scopes.Email);
        identity.SetClaim(Claims.Subject, "person");
        identity.SetClaim(Claims.Name, "alice");
        identity.SetClaim(Claims.Email, "alice@example.test");
        identity.SetClaim(Claims.EmailVerified, "false");
        OidcClaimMapper.SetArrayClaim(identity, Claims.AuthenticationMethodReference, [AuthenticationMethodConstants.Password]);
        identity.SetClaim(Claims.AuthenticationTime, "1700000000");
        identity.SetClaim(StampType, "stamp");
        OidcClaimMapper.SetOrganizationClaims(identity, [Membership(First, "viewer", day: 1)], Permissions.GetValueOrDefault!, null, null);

        var info = OidcClaimMapper.ToUserInfo(new ClaimsPrincipal(identity));

        info.Keys.Should().BeEquivalentTo(Claims.Subject, Claims.Email, Claims.EmailVerified, OrganizationClaimTypeConstants.Id, OrganizationClaimTypeConstants.Role, OrganizationClaimTypeConstants.Permission);
        info[Claims.EmailVerified].Should().Be(false);
        info[OrganizationClaimTypeConstants.Permission].Should().BeEquivalentTo(new[] { $"{First}:wheelhouse:read" });
        info[Claims.Email].Should().Be("alice@example.test");
    }

    private static readonly string[] Both = [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken];

    private static ClaimsIdentity Identity(params string[] scopes) => OidcClaimMapper.CreateIdentity(Client, scopes, [], authenticatedAt: null);

    private static IEnumerable<string> Destinations(ClaimsIdentity identity, string type)
    {
        var claim = new Claim(type, "value");
        identity.AddClaim(claim);
        return OidcClaimMapper.GetDestinations(identity.Claims.Last(c => c.Type == type));
    }

    private static OrganizationMembershipModel Membership(Guid organization, string role, int day) => new()
    {
        OrganizationId = organization,
        Name = "Org",
        Slug = $"org-{day}",
        Role = role,
        JoinedAt = new DateTimeOffset(2026, 10, day, 0, 0, 0, TimeSpan.Zero),
    };
}

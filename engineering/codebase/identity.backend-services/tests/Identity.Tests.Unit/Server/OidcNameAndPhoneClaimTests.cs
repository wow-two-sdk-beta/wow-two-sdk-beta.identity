using System.Security.Claims;
using AwesomeAssertions;
using OpenIddict.Abstractions;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Server;

// The Providers package's Identity.Claims namespace would shadow OpenIddict's claim names here.
using Claims = OpenIddict.Abstractions.OpenIddictConstants.Claims;
using Destinations = OpenIddict.Abstractions.OpenIddictConstants.Destinations;
using Scopes = OpenIddict.Abstractions.OpenIddictConstants.Scopes;

/// <summary><c>name</c> always issued, a phone-only person included, and <c>phone_number</c> under the <c>phone</c> scope.</summary>
public sealed class OidcNameAndPhoneClaimTests
{
    private const string StampType = "AspNet.Identity.SecurityStamp";
    private const string Phone = "+998901234567";
    private static readonly OidcClientProfile Client = new("desk", "desk-api", PermissionNamespace: null);

    [Theory]
    [InlineData("alice", "alice@example.test", null, "alice")]
    [InlineData(null, "alice@example.test", null, "alice@example.test")]
    [InlineData(Phone, null, Phone, Phone)]
    [InlineData("phone-0190a2b3c4d5", null, Phone, Phone)]
    [InlineData("bob", null, null, "bob")]
    public void Name_ShouldAlwaysBeIssued_FallingBackToThePhoneWithoutAnEmail(string? userName, string? email, string? phone, string expected)
    {
        var user = new IdentityUser { Id = Guid.CreateVersion7(), UserName = userName, Email = email, PhoneNumber = phone };
        var identity = OidcClaimMapper.CreateIdentity(Client, [Scopes.OpenId, Scopes.Profile], [], authenticatedAt: null);

        OidcClaimMapper.SetPersonClaims(identity, user, StampType);

        identity.GetClaims(Claims.Name).Should().Equal(expected);
    }

    [Fact]
    public void Name_ShouldFallBackToTheId_ForAPersonWithNothingElse()
    {
        var user = new IdentityUser { Id = Guid.CreateVersion7() };

        OidcClaimMapper.DisplayNameOf(user).Should().Be(user.Id.ToString("D"));
    }

    [Fact]
    public void Phone_ShouldTravelWithItsVerification_UnderThePhoneScopeOnly()
    {
        var user = new IdentityUser { Id = Guid.CreateVersion7(), UserName = Phone, PhoneNumber = Phone, IsPhoneNumberConfirmed = true };
        var withPhone = OidcClaimMapper.CreateIdentity(Client, [Scopes.OpenId, Scopes.Phone], [], authenticatedAt: null);
        var without = OidcClaimMapper.CreateIdentity(Client, [Scopes.OpenId, Scopes.Profile], [], authenticatedAt: null);

        OidcClaimMapper.SetPersonClaims(withPhone, user, StampType);
        OidcClaimMapper.SetPersonClaims(without, user, StampType);

        withPhone.GetClaim(Claims.PhoneNumber).Should().Be(Phone);
        withPhone.FindFirst(Claims.PhoneNumberVerified)!.ValueType.Should().Be(ClaimValueTypes.Boolean);
        OidcClaimMapper.GetDestinations(withPhone.FindFirst(Claims.PhoneNumber)!).Should().BeEquivalentTo(Destinations.AccessToken, Destinations.IdentityToken);
        OidcClaimMapper.GetDestinations(withPhone.FindFirst(Claims.PhoneNumberVerified)!).Should().BeEquivalentTo(Destinations.AccessToken, Destinations.IdentityToken);
        OidcClaimMapper.GetDestinations(without.FindFirst(Claims.PhoneNumber)!).Should().BeEmpty("without the phone scope the number stays out of the tokens");
        withPhone.HasClaim(claim => claim.Type == Claims.Email).Should().BeFalse("a phone-only person has no email");
    }

    [Fact]
    public void UserInfo_ShouldAnswerThePhoneAndItsVerificationAsABoolean()
    {
        var user = new IdentityUser { Id = Guid.CreateVersion7(), UserName = Phone, PhoneNumber = Phone, IsPhoneNumberConfirmed = true };
        var identity = OidcClaimMapper.CreateIdentity(Client, [Scopes.OpenId, Scopes.Profile, Scopes.Phone], [], authenticatedAt: null);
        OidcClaimMapper.SetPersonClaims(identity, user, StampType);
        identity.SetDestinations(OidcClaimMapper.GetDestinations);

        var info = OidcClaimMapper.ToUserInfo(new ClaimsPrincipal(identity));

        info[Claims.PhoneNumber].Should().Be(Phone);
        info[Claims.PhoneNumberVerified].Should().Be(true);
        info[Claims.Name].Should().Be(Phone);
    }

    [Fact]
    public void Refresh_ShouldDropAPhoneThePersonRemoved()
    {
        var identity = OidcClaimMapper.CreateIdentity(Client, [Scopes.OpenId, Scopes.Phone], [], authenticatedAt: null);
        OidcClaimMapper.SetPersonClaims(identity, new IdentityUser { Id = Guid.CreateVersion7(), UserName = "carol", PhoneNumber = Phone }, StampType);

        OidcClaimMapper.SetPersonClaims(identity, new IdentityUser { Id = Guid.CreateVersion7(), UserName = "carol" }, StampType);

        identity.HasClaim(claim => claim.Type is Claims.PhoneNumber or Claims.PhoneNumberVerified).Should().BeFalse();
    }
}

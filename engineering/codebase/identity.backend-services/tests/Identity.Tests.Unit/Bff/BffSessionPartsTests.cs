using System.Security.Claims;
using System.Text;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Bff;
using WoW.Two.Sdk.Beta.Be.Identity.Bff.Antiforgery;
using WoW.Two.Sdk.Beta.Be.Identity.Bff.Endpoints;
using WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Bff;

/// <summary>The session's parts: the encrypted payload, the cache store, the reference-only cookie, the return URL, the antiforgery token and the session answer.</summary>
public sealed class BffSessionPartsTests
{
    private const string AccessToken = "access-token-value-that-must-stay-server-side";
    private static readonly IDataProtectionProvider Protection = new EphemeralDataProtectionProvider();

    [Fact]
    public void Protector_ShouldEncryptTheTokensAndClaims_AndReadThemBack()
    {
        var protector = new BffSessionProtector(Protection);
        var state = State();

        var payload = protector.Protect(state);

        Encoding.UTF8.GetString(payload).Should().NotContain(AccessToken).And.NotContain("alice@example.test");
        protector.Unprotect(payload).Should().BeEquivalentTo(state);
        new BffSessionProtector(new EphemeralDataProtectionProvider()).Unprotect(payload).Should().BeNull("another key ring cannot read it");
    }

    [Fact]
    public async Task Repository_ShouldKeyOnTheSid_AndForgetARemovedSession()
    {
        var repository = new DistributedCacheBffSessionRepository(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        var record = new BffSessionRecord { SessionId = "sid-1", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), Payload = [1, 2, 3] };

        await repository.SaveAsync(record);
        var found = await repository.FindAsync("sid-1");
        await repository.RemoveAsync("sid-1");

        found!.Payload.Should().Equal(1, 2, 3);
        found.ExpiresAt.Should().Be(record.ExpiresAt);
        (await repository.FindAsync("sid-1")).Should().BeNull();
        (await repository.FindAsync("sid-2")).Should().BeNull();
    }

    [Fact]
    public void Cookie_ShouldCarryOnlyTheReference_WhateverTheTicketHolds()
    {
        var format = new BffTicketDataFormat(new TicketDataFormat(Protection.CreateProtector("test")));
        var properties = new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12), IsPersistent = true, RedirectUri = "/desk" };
        properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = AccessToken }]);
        new BffSessionReference { SessionId = "sid-1", Reference = "ref-1" }.WriteTo(properties);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "alice"), new Claim("email", "alice@example.test")], "test"));

        var cookie = format.Protect(new AuthenticationTicket(principal, properties, BffAuthenticationSchemeConstants.Cookie));
        var read = format.Unprotect(cookie)!;

        read.Principal.Claims.Should().BeEmpty();
        read.Principal.Identity!.IsAuthenticated.Should().BeTrue();
        read.Properties.GetTokens().Should().BeEmpty();
        read.Properties.RedirectUri.Should().BeNull();
        read.Properties.IsPersistent.Should().BeTrue();
        read.Properties.ExpiresUtc.Should().Be(properties.ExpiresUtc);
        BffSessionReference.From(read.Properties).Should().Be(new BffSessionReference { SessionId = "sid-1", Reference = "ref-1" });
    }

    [Fact]
    public void Cookie_ShouldRefuseATicketWithoutASession()
    {
        var format = new BffTicketDataFormat(new TicketDataFormat(Protection.CreateProtector("test")));
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity("test")), BffAuthenticationSchemeConstants.Cookie);

        var protect = () => format.Protect(ticket);

        protect.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("/desk", "/desk")]
    [InlineData("/desk?tab=2#top", "/desk?tab=2#top")]
    [InlineData("https://evil.example.test/", "/")]
    [InlineData("//evil.example.test/", "/")]
    [InlineData("/\\evil.example.test/", "/")]
    [InlineData("desk", "/")]
    [InlineData("/de\nsk", "/")]
    [InlineData(null, "/")]
    public void ReturnUrl_ShouldKeepLocalPathsOnly(string? requested, string expected)
        => BffReturnUrl.Normalize(requested).Should().Be(expected);

    [Fact]
    public void CsrfToken_ShouldBindToTheSid()
    {
        var csrf = new BffCsrfTokenService(Protection);

        var token = csrf.Create("sid-1");

        csrf.IsValid("sid-1", token).Should().BeTrue();
        csrf.IsValid("sid-2", token).Should().BeFalse("a token of another session fails");
        csrf.IsValid("sid-1", null).Should().BeFalse();
        csrf.IsValid("sid-1", "not-a-token").Should().BeFalse();
        new BffCsrfTokenService(new EphemeralDataProtectionProvider()).IsValid("sid-1", token).Should().BeFalse();
    }

    [Fact]
    public void SessionAnswer_ShouldCarryThePersonTheActiveOrganizationAndItsPermissions()
    {
        var organization = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var other = Guid.Parse("01900000-0000-7000-8000-000000000002");
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "person-1"), new Claim("name", "+998901234567"), new Claim("phone_number", "+998901234567"),
            new Claim("org_id", organization.ToString()), new Claim("org_role", $"{organization}:owner"),
            new Claim("org_permission", $"{organization}:desk:edit"), new Claim("org_permission", $"{organization}:desk:read"),
            new Claim("org_permission", $"{other}:desk:delete"),
        ], "test"));
        var expiresAt = DateTimeOffset.UtcNow.AddHours(12);

        var dto = BffSessionMapper.ToDto(principal, "csrf", expiresAt);

        dto.IsAuthenticated.Should().BeTrue();
        dto.Person.Should().Be(new BffPersonDto { Sub = "person-1", Name = "+998901234567", Email = null, PhoneNumber = "+998901234567" });
        dto.Organization.Should().Be(new BffOrganizationDto { Id = organization, Role = "owner" });
        dto.Permissions.Should().Equal("desk:edit", "desk:read");
        dto.CsrfToken.Should().Be("csrf");
        dto.ExpiresAt.Should().Be(expiresAt);
        BffSessionMapper.ToDto(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "p")], "test")), "csrf", expiresAt)
            .Organization.Should().BeNull("a person without a membership has no active organization");
    }

    private static BffSessionState State() => new()
    {
        Reference = "ref-1",
        Claims = [new BffSessionClaim { Type = "sub", Value = "alice", ValueType = ClaimValueTypes.String }, new BffSessionClaim { Type = "email", Value = "alice@example.test", ValueType = ClaimValueTypes.String }],
        AccessToken = AccessToken,
        AccessTokenExpiresAt = DateTimeOffset.UnixEpoch.AddMinutes(10),
        RefreshToken = "refresh",
        IdentityToken = "id",
        SignedInAt = DateTimeOffset.UnixEpoch,
        ExpiresAt = DateTimeOffset.UnixEpoch.AddHours(12),
    };
}

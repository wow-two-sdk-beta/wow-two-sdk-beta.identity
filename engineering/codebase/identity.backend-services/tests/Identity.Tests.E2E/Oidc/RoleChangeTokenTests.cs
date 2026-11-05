using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;

namespace Identity.Tests.E2E.Oidc;

/// <summary>
/// A role change reaches an app with no sign-in across the real service: once a manager re-roles a member, the app's next
/// refresh carries the new role, the cookie stays and the session stays live. An instance role reaches no app's token and
/// ends nothing.
/// </summary>
[Collection(OidcHostCollection.Name)]
public sealed class RoleChangeTokenTests(OidcHostFixture fixture)
{
    private const string Password = "e2e-correct-horse-battery";

    [Fact]
    public async Task OrganizationRoleChange_ShouldGiveFreshClaimsAtTheNextRefresh_WithNoSignIn()
    {
        using var owner = fixture.Server.CreateClient();
        var ownerId = await owner.SignUpAndSignInAsync($"owner-{Guid.NewGuid():N}@example.test", Password);
        var organization = await CreateOrganizationAsync(ownerId);
        using var member = fixture.Server.CreateClient();
        var email = $"member-{Guid.NewGuid():N}@example.test";
        var memberId = await member.SignUpAndSignInAsync(email, Password);
        await AddMemberAsync(organization, memberId, OrganizationRoleConstants.Editor);
        var tokens = await member.SignInToAppAsync();
        (await member.ValidateAsync(tokens.IdentityToken, OidcHost.ClientId)).GetClaim("org_role").Value.Should().Be($"{organization}:editor");

        using (var changed = await owner.Http.PutAsJsonAsync($"/api/organizations/{organization}/members/{memberId}", new { role = OrganizationRoleConstants.Viewer }))
            changed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var fresh = await TokenSet.ReadAsync(await member.RefreshAsync(tokens.RefreshToken));
        (await member.ValidateAsync(fresh.IdentityToken, OidcHost.ClientId)).GetClaim("org_role").Value.Should().Be($"{organization}:viewer");
        (await member.ValidateAsync(fresh.AccessToken, OidcHost.Audience)).GetClaim("org_role").Value.Should().Be($"{organization}:viewer");
        (await member.Http.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.OK, "the cookie stays");
        (await ReasonsAsync(memberId)).Should().OnlyContain(reason => reason == null, "no session ended");
    }

    [Fact]
    public async Task MemberRemoval_ShouldDropTheOrganizationAtTheNextRefresh_WithNoSignIn()
    {
        using var owner = fixture.Server.CreateClient();
        var ownerId = await owner.SignUpAndSignInAsync($"owner-{Guid.NewGuid():N}@example.test", Password);
        var organization = await CreateOrganizationAsync(ownerId);
        using var member = fixture.Server.CreateClient();
        var memberId = await member.SignUpAndSignInAsync($"gone-{Guid.NewGuid():N}@example.test", Password);
        await AddMemberAsync(organization, memberId, OrganizationRoleConstants.Editor);
        var tokens = await member.SignInToAppAsync();

        using (var removed = await owner.Http.DeleteAsync($"/api/organizations/{organization}/members/{memberId}"))
            removed.IsSuccessStatusCode.Should().BeTrue();

        var fresh = await TokenSet.ReadAsync(await member.RefreshAsync(tokens.RefreshToken));
        (await member.ValidateAsync(fresh.AccessToken, OidcHost.Audience)).TryGetClaim("org_id", out _).Should().BeFalse();
    }

    [Fact]
    public async Task InstanceRoleGrant_ShouldEndNothing()
    {
        using var client = fixture.Server.CreateClient();
        var userId = await client.SignUpAndSignInAsync($"grant-{Guid.NewGuid():N}@example.test", Password);
        var tokens = await client.SignInToAppAsync();

        await using (var scope = fixture.Server.Host.Services.CreateAsyncScope())
        {
            var person = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(userId))!;
            (await scope.ServiceProvider.GetRequiredService<InstanceRoleService>().GrantAsync(person, InstanceRoleConstants.Support)).IsGranted.Should().BeTrue();
        }

        (await client.RefreshAsync(tokens.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReasonsAsync(userId)).Should().OnlyContain(reason => reason == null);
    }

    private async Task<Guid> CreateOrganizationAsync(Guid ownerId)
    {
        await using var scope = fixture.Server.Host.Services.CreateAsyncScope();
        var owner = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(ownerId))!;
        var organization = new IdentityOrganization { Name = "Roles", Slug = $"roles-{Guid.NewGuid():N}"[..20] };
        (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateAsync(organization, owner)).IsSuccess.Should().BeTrue();
        return organization.Id;
    }

    private async Task AddMemberAsync(Guid organizationId, Guid userId, string role)
    {
        await using var scope = fixture.Server.Host.Services.CreateAsyncScope();
        var organizations = scope.ServiceProvider.GetRequiredService<OrganizationService>();
        var person = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(userId))!;
        (await organizations.AddMemberAsync((await organizations.FindByIdAsync(organizationId))!, person, role)).IsSuccess.Should().BeTrue();
    }

    /// <summary>Why each of the person's sessions ended; null for a live one.</summary>
    private async Task<List<string?>> ReasonsAsync(Guid userId)
    {
        await using var scope = fixture.Server.Host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Sessions.AsNoTracking()
            .Where(session => session.UserId == userId)
            .Select(session => session.RevokeReason)
            .ToListAsync();
    }
}

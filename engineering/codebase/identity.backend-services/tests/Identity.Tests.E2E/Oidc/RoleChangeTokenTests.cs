using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;

namespace Identity.Tests.E2E.Oidc;

/// <summary>
/// A role change signs the person out of every app across the real service: once a manager re-roles a member, or the
/// instance grants them a role, the app's refresh is refused (<c>invalid_grant</c>), the cookie ends and the session is
/// marked <c>roles_changed</c>; the app signs in again and its new tokens carry the new role. The manager stays signed in.
/// </summary>
[Collection(OidcHostCollection.Name)]
public sealed class RoleChangeTokenTests(OidcHostFixture fixture)
{
    private const string Password = "e2e-correct-horse-battery";

    [Fact]
    public async Task OrganizationRoleChange_ShouldRefuseTheAppsRefresh_SoTheAppSignsInAgainWithTheNewRole()
    {
        using var owner = fixture.Server.CreateClient();
        var ownerId = await owner.SignUpAndSignInAsync($"owner-{Guid.NewGuid():N}@example.test", Password);
        var organization = await CreateOrganizationAsync(ownerId);
        using var member = fixture.Server.CreateClient();
        var email = $"member-{Guid.NewGuid():N}@example.test";
        var memberId = await member.SignUpAndSignInAsync(email, Password);
        await AddMemberAsync(organization, memberId, OrganizationRoleConstants.Editor);
        await member.SignInAsync(email, Password);
        var tokens = await member.SignInToAppAsync();
        (await member.ValidateAsync(tokens.IdentityToken, OidcHost.ClientId)).GetClaim("org_role").Value.Should().Be($"{organization}:editor");
        var ownerTokens = await owner.SignInToAppAsync();

        using (var changed = await owner.Http.PutAsJsonAsync($"/api/organizations/{organization}/members/{memberId}", new { role = OrganizationRoleConstants.Viewer }))
            changed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var refused = await member.RefreshAsync(tokens.RefreshToken);
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the app's tokens still carry the old role");
        (await refused.Content.ReadAsStringAsync()).Should().Contain("invalid_grant");
        (await member.Http.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the cookie ended with its session");
        (await ReasonsAsync(memberId)).Should().OnlyContain(reason => reason == SessionRevokeReasonConstants.RolesChanged);
        (await GrantStatusesAsync(memberId)).Should().OnlyContain(status => status == OpenIddictConstants.Statuses.Revoked);

        await member.SignInAsync(email, Password);
        var fresh = await member.SignInToAppAsync();
        (await member.ValidateAsync(fresh.IdentityToken, OidcHost.ClientId)).GetClaim("org_role").Value.Should().Be($"{organization}:viewer");
        (await member.RefreshAsync(fresh.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await owner.RefreshAsync(ownerTokens.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK, "the manager's own roles did not change");
        (await owner.Http.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InstanceRoleGrant_ShouldRefuseTheAppsRefresh()
    {
        using var client = fixture.Server.CreateClient();
        var userId = await client.SignUpAndSignInAsync($"grant-{Guid.NewGuid():N}@example.test", Password);
        var tokens = await client.SignInToAppAsync();

        await using (var scope = fixture.Server.Host.Services.CreateAsyncScope())
        {
            var person = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(userId))!;
            (await scope.ServiceProvider.GetRequiredService<InstanceRoleService>().GrantAsync(person, InstanceRoleConstants.Support)).IsGranted.Should().BeTrue();
        }

        using var refused = await client.RefreshAsync(tokens.RefreshToken);
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("invalid_grant");
        (await ReasonsAsync(userId)).Should().OnlyContain(reason => reason == SessionRevokeReasonConstants.RolesChanged);
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

    /// <summary>The status of every authorization and token issued to the person.</summary>
    private async Task<List<string?>> GrantStatusesAsync(Guid userId)
    {
        await using var scope = fixture.Server.Host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var subject = userId.ToString();
        var authorizations = await context.OidcAuthorizations.Where(a => a.Subject == subject).Select(a => a.Status).ToListAsync();
        var tokens = await context.OidcTokens.Where(t => t.Subject == subject).Select(t => t.Status).ToListAsync();
        return [.. authorizations, .. tokens];
    }
}

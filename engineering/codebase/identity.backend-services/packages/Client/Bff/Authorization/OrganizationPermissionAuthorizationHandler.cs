using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Authorization;

/// <summary>
/// Validates <see cref="OrganizationPermissionRequirement"/>: the principal's active organization (<c>org_id</c>) grants
/// the permission, as an <c>org_permission</c> value <c>{org_id}:{permission}</c>.
/// </summary>
internal sealed class OrganizationPermissionAuthorizationHandler : AuthorizationHandler<OrganizationPermissionRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OrganizationPermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);
        if (Guid.TryParse(context.User.FindFirst(OrganizationClaimTypeConstants.Id)?.Value, CultureInfo.InvariantCulture, out var organization)
            && context.User.HasOrganizationPermission(organization, requirement.Permission))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

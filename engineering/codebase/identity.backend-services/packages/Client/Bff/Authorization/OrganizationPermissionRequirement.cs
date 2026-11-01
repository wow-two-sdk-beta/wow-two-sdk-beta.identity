using Microsoft.AspNetCore.Authorization;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Authorization;

/// <summary>Represents a permission the session's active organization must grant (<c>org_permission</c>).</summary>
internal sealed record OrganizationPermissionRequirement : IAuthorizationRequirement
{
    /// <summary>Gets the permission, such as <c>desk:edit</c>, compared exactly.</summary>
    public required string Permission { get; init; }
}

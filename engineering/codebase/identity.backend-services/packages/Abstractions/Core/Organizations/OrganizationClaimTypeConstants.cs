namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

/// <summary>Holds the default claim types the organizations slice writes; <c>IdentityClaimOptions</c> (Module) may override them.</summary>
public static class OrganizationClaimTypeConstants
{
    /// <summary>The active organization's id; an OIDC token carries one active organization (<c>org_id</c>).</summary>
    public const string Id = "org_id";

    /// <summary>One claim per membership, valued <c>{organizationId}:{role}</c>.</summary>
    public const string Role = "org_role";

    /// <summary>One claim per granted permission, valued <c>{organizationId}:{permission}</c>.</summary>
    public const string Permission = "org_permission";
}

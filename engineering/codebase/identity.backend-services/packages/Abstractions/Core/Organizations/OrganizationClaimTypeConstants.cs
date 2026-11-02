namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

/// <summary>Holds the default claim types the organizations slice writes; <c>IdentityClaimOptions</c> (Module) may override them.</summary>
public static class OrganizationClaimTypeConstants
{
    /// <summary>The active organization's id; an OIDC token carries one active organization (<c>org_id</c>).</summary>
    public const string Id = "org_id";

    /// <summary>The active organization's display name (<c>org_name</c>), beside <see cref="Id"/>.</summary>
    public const string Name = "org_name";

    /// <summary>The active organization's handle (<c>org_slug</c>), such as <c>ocharo</c>, beside <see cref="Id"/>.</summary>
    public const string Slug = "org_slug";

    /// <summary>One claim per membership, valued <c>{organizationId}:{role}</c>.</summary>
    public const string Role = "org_role";

    /// <summary>One claim per granted permission, valued <c>{organizationId}:{permission}</c>.</summary>
    public const string Permission = "org_permission";
}

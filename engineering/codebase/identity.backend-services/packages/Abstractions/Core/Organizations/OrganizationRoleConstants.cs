namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

/// <summary>
/// Holds the fixed organization role catalog every instance carries; an app adds namespaced permissions to these roles
/// (<c>Identity:Organizations:Roles:{role}</c>), never a role of its own.
/// </summary>
public static class OrganizationRoleConstants
{
    /// <summary>Owns the organization: everything an admin does, plus inviting, promoting, demoting and removing owners.</summary>
    public const string Owner = "owner";

    /// <summary>Manages members and invitations, short of owners.</summary>
    public const string Admin = "admin";

    /// <summary>Works on the organization's content.</summary>
    public const string Editor = "editor";

    /// <summary>Reads the organization's content.</summary>
    public const string Viewer = "viewer";

    /// <summary>Handles the organization's billing.</summary>
    public const string Billing = "billing";

    /// <summary>Every role in the catalog, owner first.</summary>
    public static readonly IReadOnlyList<string> All = [Owner, Admin, Editor, Viewer, Billing];

    /// <summary>The catalog's spelling of <paramref name="role"/> (case-insensitive); null when the catalog has no such role.</summary>
    /// <param name="role">The role name.</param>
    public static string? Find(string? role)
        => string.IsNullOrWhiteSpace(role) ? null : All.FirstOrDefault(name => string.Equals(name, role.Trim(), StringComparison.OrdinalIgnoreCase));
}

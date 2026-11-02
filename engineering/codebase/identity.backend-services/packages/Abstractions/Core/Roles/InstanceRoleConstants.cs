namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;

/// <summary>
/// Holds the fixed instance role catalog: roles over the instance itself, held beside any organization role. They open the
/// instance's console and never enter an app's tokens.
/// </summary>
public static class InstanceRoleConstants
{
    /// <summary>Runs the instance: every console area.</summary>
    public const string Admin = "admin";

    /// <summary>Helps people: reads accounts and ends sessions.</summary>
    public const string Support = "support";

    /// <summary>Reads the sign-in log and settings, changing nothing.</summary>
    public const string Auditor = "auditor";

    /// <summary>Every role in the catalog, admin first.</summary>
    public static readonly IReadOnlyList<string> All = [Admin, Support, Auditor];

    /// <summary>The catalog's spelling of <paramref name="role"/> (case-insensitive); null when the catalog has no such role.</summary>
    /// <param name="role">The role name.</param>
    public static string? Find(string? role)
        => string.IsNullOrWhiteSpace(role) ? null : All.FirstOrDefault(name => string.Equals(name, role.Trim(), StringComparison.OrdinalIgnoreCase));
}

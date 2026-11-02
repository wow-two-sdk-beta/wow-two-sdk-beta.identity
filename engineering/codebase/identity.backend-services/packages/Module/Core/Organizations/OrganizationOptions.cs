namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

/// <summary>Holds the permissions each role of the fixed catalog (<see cref="OrganizationRoleConstants"/>) grants.</summary>
/// <remarks>Set in code with <c>.AddOrganizations(o => …)</c> or in the host section <c>Identity:Organizations</c>, which is applied last.</remarks>
public sealed record OrganizationOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Identity:Organizations";

    /// <summary>The role an organization's creator receives (<see cref="OrganizationRoleConstants.Owner"/>).</summary>
    public string OwnerRole => OrganizationRoleConstants.Owner;

    /// <summary>The role that manages members and invitations beside the owner, short of owners (<see cref="OrganizationRoleConstants.Admin"/>).</summary>
    public string AdminRole => OrganizationRoleConstants.Admin;

    /// <summary>
    /// Gets the permissions each catalog role grants, such as <c>editor</c> → <c>desk:orders:edit</c>. Names compare
    /// case-insensitively; every catalog role exists whether listed or not, and a name outside the catalog refuses the boot.
    /// </summary>
    public Dictionary<string, List<string>> Roles { get; } = new(StringComparer.OrdinalIgnoreCase);
}

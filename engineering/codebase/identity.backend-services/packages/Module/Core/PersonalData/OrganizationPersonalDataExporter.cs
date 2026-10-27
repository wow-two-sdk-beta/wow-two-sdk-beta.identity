using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;

/// <summary>Exports the user's memberships as the <c>organizations</c> section; registered by <c>AddOrganizations</c>.</summary>
/// <param name="organizations">Reads the user's memberships.</param>
public sealed class OrganizationPersonalDataExporter(OrganizationService organizations) : IPersonalDataExporter
{
    /// <inheritdoc />
    public string Section => "organizations";

    /// <inheritdoc />
    public async Task<object?> ExportAsync(IdentityUser user, CancellationToken cancellationToken = default)
        => (await organizations.ListForUserAsync(user, cancellationToken)).Select(m => new { m.Name, m.Slug, m.Role, m.JoinedAt }).ToList();
}

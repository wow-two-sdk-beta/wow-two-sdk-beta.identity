using WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;

/// <summary>Exports the names of the user's roles as the <c>roles</c> section; registered by <c>AddRoles</c>.</summary>
/// <param name="roles">Reads the user's roles.</param>
public sealed class RolePersonalDataExporter(UserRoleService roles) : IPersonalDataExporter
{
    /// <inheritdoc />
    public string Section => "roles";

    /// <inheritdoc />
    public async Task<object?> ExportAsync(IdentityUser user, CancellationToken cancellationToken = default)
        => await roles.GetRolesAsync(user, cancellationToken);
}

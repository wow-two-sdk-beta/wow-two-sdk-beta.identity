namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;

/// <summary>
/// Provides role membership: add, remove and query a user's roles by name. Removal rotates the security stamp, so
/// principals still carrying the role end; an added role appears from the next sign-in.
/// </summary>
/// <param name="roles">The role service, for name lookup.</param>
/// <param name="memberships">The membership repository.</param>
/// <param name="accounts">The core account service, for stamp rotation.</param>
public sealed class UserRoleService(
    RoleService roles,
    IUserRoleRepository memberships,
    UserAccountService accounts)
{
    /// <summary>Add <paramref name="user"/> to the named role.</summary>
    /// <param name="user">The user.</param>
    /// <param name="roleName">The role name (case-insensitive).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentityResult> AddToRoleAsync(IdentityUser user, string roleName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (await roles.FindByNameAsync(roleName, cancellationToken) is not { } role)
            return RoleNotFound(roleName);
        if (await memberships.ContainsAsync(user.Id, role.Id, cancellationToken))
            return IdentityUserExtensions.Failure(IdentityErrorCodeConstants.UserAlreadyInRole, $"The user already holds role '{roleName}'.", IdentityUserExtensions.Param("RoleName", roleName));

        await memberships.AddAsync(user.Id, role.Id, cancellationToken);
        return IdentityResult.Success;
    }

    /// <summary>Remove <paramref name="user"/> from the named role and rotate the security stamp.</summary>
    /// <param name="user">The user.</param>
    /// <param name="roleName">The role name (case-insensitive).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentityResult> RemoveFromRoleAsync(IdentityUser user, string roleName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (await roles.FindByNameAsync(roleName, cancellationToken) is not { } role)
            return RoleNotFound(roleName);
        if (!await memberships.ContainsAsync(user.Id, role.Id, cancellationToken))
            return IdentityUserExtensions.Failure(IdentityErrorCodeConstants.UserNotInRole, $"The user does not hold role '{roleName}'.", IdentityUserExtensions.Param("RoleName", roleName));

        await memberships.RemoveAsync(user.Id, role.Id, cancellationToken);
        return await accounts.RotateSecurityStampAsync(user, cancellationToken);
    }

    /// <summary>Whether <paramref name="user"/> holds the named role.</summary>
    /// <param name="user">The user.</param>
    /// <param name="roleName">The role name (case-insensitive).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> IsInRoleAsync(IdentityUser user, string roleName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        return await roles.FindByNameAsync(roleName, cancellationToken) is { } role
            && await memberships.ContainsAsync(user.Id, role.Id, cancellationToken);
    }

    /// <summary>The names of the user's roles, ordered.</summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<IReadOnlyList<string>> GetRolesAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        return memberships.GetRoleNamesAsync(user.Id, cancellationToken);
    }

    /// <summary>The ids of the named role's members; empty when the role does not exist.</summary>
    /// <param name="roleName">The role name (case-insensitive).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<Guid>> GetUserIdsInRoleAsync(string roleName, CancellationToken cancellationToken = default)
        => await roles.FindByNameAsync(roleName, cancellationToken) is { } role
            ? await memberships.GetUserIdsAsync(role.Id, cancellationToken)
            : [];

    private static IdentityResult RoleNotFound(string roleName)
        => IdentityUserExtensions.Failure(IdentityErrorCodeConstants.RoleNotFound, $"Role '{roleName}' does not exist.", IdentityUserExtensions.Param("RoleName", roleName));
}

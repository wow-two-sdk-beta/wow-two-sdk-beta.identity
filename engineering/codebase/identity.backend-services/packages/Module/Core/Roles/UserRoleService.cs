using WoW.Two.Sdk.Beta.Be.Identity.Core.SecurityStamps;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;

/// <summary>
/// Provides role membership: add, remove and query a user's roles by name. Adding or removing a role signs the person out
/// everywhere (<see cref="RoleChangeSignOut"/>), so they sign in again carrying the new roles.
/// </summary>
/// <param name="roles">The role service, for name lookup.</param>
/// <param name="memberships">The membership repository.</param>
/// <param name="accounts">The core account service, for stamp rotation.</param>
/// <param name="sessions">The sign-in sessions a role change ends; null without the sessions slice.</param>
/// <param name="stamps">The stamp check, whose cached stamp a role change drops; null without stamp validation.</param>
public sealed class UserRoleService(
    RoleService roles,
    IUserRoleRepository memberships,
    UserAccountService accounts,
    UserSessionService? sessions = null,
    SecurityStampValidator? stamps = null)
{
    /// <summary>Add <paramref name="user"/> to the named role and sign them out everywhere (<see cref="RoleChangeSignOut"/>).</summary>
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
        return await RoleChangeSignOut.ApplyAsync(user, accounts, sessions, stamps, cancellationToken);
    }

    /// <summary>Remove <paramref name="user"/> from the named role and sign them out everywhere (<see cref="RoleChangeSignOut"/>).</summary>
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
        return await RoleChangeSignOut.ApplyAsync(user, accounts, sessions, stamps, cancellationToken);
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

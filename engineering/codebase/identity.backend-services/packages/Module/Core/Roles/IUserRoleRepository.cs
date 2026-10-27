using System.Security.Claims;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;

/// <summary>Defines role membership persistence, keyed by user and role ids.</summary>
public interface IUserRoleRepository
{
    /// <summary>Add a membership.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="roleId">The role id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>Remove a membership.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="roleId">The role id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>Whether the membership exists.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="roleId">The role id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> ContainsAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>The names of the user's roles.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<string>> GetRoleNamesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The claims attached to any of the user's roles.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Claim>> GetRoleClaimsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The ids of the role's members.</summary>
    /// <param name="roleId">The role id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Guid>> GetUserIdsAsync(Guid roleId, CancellationToken cancellationToken = default);
}

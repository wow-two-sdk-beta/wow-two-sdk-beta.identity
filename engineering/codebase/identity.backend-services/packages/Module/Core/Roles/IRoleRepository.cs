using System.Security.Claims;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;

/// <summary>Defines role persistence: the role rows and the claims attached to each role.</summary>
public interface IRoleRepository
{
    /// <summary>Persist a new role.</summary>
    /// <param name="role">The role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateAsync(IdentityRole role, CancellationToken cancellationToken = default);

    /// <summary>Persist changes to a role.</summary>
    /// <param name="role">The role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateAsync(IdentityRole role, CancellationToken cancellationToken = default);

    /// <summary>Delete a role with its claims and memberships.</summary>
    /// <param name="role">The role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(IdentityRole role, CancellationToken cancellationToken = default);

    /// <summary>Find a role by key, or null.</summary>
    /// <param name="roleId">The role id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IdentityRole?> FindByIdAsync(Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>Find a role by normalized name, or null.</summary>
    /// <param name="normalizedName">The normalized role name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IdentityRole?> FindByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken = default);

    /// <summary>The claims attached to a role.</summary>
    /// <param name="roleId">The role id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Claim>> GetClaimsAsync(Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>Attach a claim to a role.</summary>
    /// <param name="roleId">The role id.</param>
    /// <param name="claim">The claim.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddClaimAsync(Guid roleId, Claim claim, CancellationToken cancellationToken = default);

    /// <summary>Detach every claim of the same type and value from a role.</summary>
    /// <param name="roleId">The role id.</param>
    /// <param name="claim">The claim to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveClaimAsync(Guid roleId, Claim claim, CancellationToken cancellationToken = default);
}

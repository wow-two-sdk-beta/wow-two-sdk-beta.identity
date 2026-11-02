namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;

/// <summary>Defines the seam reaching stored invitations.</summary>
public interface IInvitationRepository
{
    /// <summary>Stores a new invitation.</summary>
    /// <param name="invitation">The invitation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(IdentityInvitation invitation, CancellationToken cancellationToken = default);

    /// <summary>The invitation whose token has the digest, or null.</summary>
    /// <param name="tokenHash">The hex SHA-256 of the token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IdentityInvitation?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>The invitation with the id, or null.</summary>
    /// <param name="id">The invitation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IdentityInvitation?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The organization's invitations, newest first.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<IdentityInvitation>> ListAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>Mark the invitation redeemed by <paramref name="userId"/>, only while it is unused, standing and before its end.</summary>
    /// <param name="id">The invitation.</param>
    /// <param name="userId">The person redeeming it.</param>
    /// <param name="at">The instant of redemption.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether this call spent it.</returns>
    Task<bool> TryAcceptAsync(Guid id, Guid userId, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>Withdraw the organization's invitation, only while it is unused and standing.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="id">The invitation.</param>
    /// <param name="at">The instant of withdrawal.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether this call withdrew it.</returns>
    Task<bool> TryRevokeAsync(Guid organizationId, Guid id, DateTimeOffset at, CancellationToken cancellationToken = default);
}

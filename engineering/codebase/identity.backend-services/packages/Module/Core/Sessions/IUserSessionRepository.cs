namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;

/// <summary>Defines session persistence: create, read, touch and revoke.</summary>
public interface IUserSessionRepository
{
    /// <summary>Store a new session.</summary>
    /// <param name="session">The row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(IdentitySession session, CancellationToken cancellationToken = default);

    /// <summary>The session, or null.</summary>
    /// <param name="id">The session id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IdentitySession?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The person's unrevoked sessions that have not expired at <paramref name="at"/>, newest first.</summary>
    /// <param name="userId">The person.</param>
    /// <param name="at">The instant expiry is judged at.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<IdentitySession>> ListUnrevokedAsync(Guid userId, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>Record a use of the session, and move a live session's end to <paramref name="expiresAt"/> when that is later.</summary>
    /// <param name="id">The session id.</param>
    /// <param name="at">The instant of use.</param>
    /// <param name="expiresAt">A sliding session's new end; null keeps the end.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task TouchAsync(Guid id, DateTimeOffset at, DateTimeOffset? expiresAt = null, CancellationToken cancellationToken = default);

    /// <summary>Bind a still-unrevoked session to a new security stamp, so it outlives the rotation that made the stamp.</summary>
    /// <param name="id">The session id.</param>
    /// <param name="securityStamp">The person's current stamp.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetSecurityStampAsync(Guid id, string? securityStamp, CancellationToken cancellationToken = default);

    /// <summary>Mark the still-unrevoked sessions among <paramref name="ids"/> revoked; returns the ones this call revoked.</summary>
    /// <param name="ids">The session ids.</param>
    /// <param name="at">The revocation instant.</param>
    /// <param name="reason">Why, one of <see cref="SessionRevokeReasonConstants"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<IdentitySession>> RevokeAsync(IReadOnlyCollection<Guid> ids, DateTimeOffset at, string reason, CancellationToken cancellationToken = default);
}

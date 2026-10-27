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

    /// <summary>Record a use of the session.</summary>
    /// <param name="id">The session id.</param>
    /// <param name="at">The instant of use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task TouchAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>Mark the still-unrevoked sessions among <paramref name="ids"/> revoked; returns the ones this call revoked.</summary>
    /// <param name="ids">The session ids.</param>
    /// <param name="at">The revocation instant.</param>
    /// <param name="reason">Why, one of <see cref="SessionRevokeReasonConstants"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<IdentitySession>> RevokeAsync(IReadOnlyCollection<Guid> ids, DateTimeOffset at, string reason, CancellationToken cancellationToken = default);
}

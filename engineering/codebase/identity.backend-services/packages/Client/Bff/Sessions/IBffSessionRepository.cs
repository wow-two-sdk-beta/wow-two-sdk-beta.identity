namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

/// <summary>
/// Defines the persistence seam for BFF sessions, keyed on the session's <c>sid</c>; defaults to the distributed cache
/// (<see cref="DistributedCacheBffSessionRepository"/>). A record's payload arrives already encrypted with Data
/// Protection, so a store never sees a token or a claim. Several instances share sessions through a shared store and a
/// shared Data Protection key ring.
/// </summary>
public interface IBffSessionRepository
{
    /// <summary>The session stored under <paramref name="sessionId"/>; <c>null</c> when none, or once it expired.</summary>
    /// <param name="sessionId">The session's <c>sid</c>.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<BffSessionRecord?> FindAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Stores <paramref name="record"/>, replacing any session under its id; the store may drop it after <see cref="BffSessionRecord.ExpiresAt"/>.</summary>
    /// <param name="record">The session.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SaveAsync(BffSessionRecord record, CancellationToken cancellationToken = default);

    /// <summary>Removes the session stored under <paramref name="sessionId"/>; a missing one is no error.</summary>
    /// <param name="sessionId">The session's <c>sid</c>.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default);
}

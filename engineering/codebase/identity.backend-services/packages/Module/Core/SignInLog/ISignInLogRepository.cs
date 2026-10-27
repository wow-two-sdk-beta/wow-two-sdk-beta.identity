namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;

/// <summary>Defines sign-in log persistence: append, attach a session, delete past the retention.</summary>
public interface ISignInLogRepository
{
    /// <summary>Append an entry; returns its id.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<long> AddAsync(IdentitySignInLogEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Record the session a successful attempt started.</summary>
    /// <param name="entryId">The entry id.</param>
    /// <param name="sessionId">The session id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetSessionAsync(long entryId, Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>The entries naming <paramref name="userId"/>, newest first.</summary>
    /// <param name="userId">The person.</param>
    /// <param name="take">How many entries at most.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<IdentitySignInLogEntry>> ListForUserAsync(Guid userId, int take, CancellationToken cancellationToken = default);

    /// <summary>Delete the entries that happened before <paramref name="before"/>; returns how many were deleted.</summary>
    /// <param name="before">The cut-off instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> DeleteBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken = default);
}

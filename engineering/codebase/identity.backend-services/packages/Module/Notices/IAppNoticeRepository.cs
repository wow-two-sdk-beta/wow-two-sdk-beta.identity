namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>
/// Defines persistence of app notices' requests by idempotency key and of people's mutes. Claiming a key is conditional, so
/// one key sends at most once per app while its row lives.
/// </summary>
public interface IAppNoticeRepository
{
    /// <summary>
    /// Claims <paramref name="request"/>'s app and key, pruning requests claimed before <paramref name="expiredBefore"/>. A
    /// pending row of the same body claimed before <paramref name="staleBefore"/> belongs to a send that never finished, and is
    /// taken over.
    /// </summary>
    /// <param name="request">The new pending row.</param>
    /// <param name="staleBefore">A pending row claimed before this instant is taken over.</param>
    /// <param name="expiredBefore">Rows claimed before this instant are gone.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<AppNoticeClaim> ClaimAsync(IdentityAppNotice request, DateTimeOffset staleBefore, DateTimeOffset expiredBefore, CancellationToken cancellationToken = default);

    /// <summary>Records a claimed request's outcome.</summary>
    /// <param name="id">The claimed row's id.</param>
    /// <param name="status"><see cref="AppNoticeStatusConstants.Sent"/> or <see cref="AppNoticeStatusConstants.Muted"/>.</param>
    /// <param name="messageRecordId">The ledger row of the sent notice; null when muted.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task CompleteAsync(Guid id, string status, Guid? messageRecordId, CancellationToken cancellationToken = default);

    /// <summary>Gives a claimed key back, so a retry may send: the person had no link, or the send failed.</summary>
    /// <param name="id">The claimed row's id.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task ReleaseAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Whether the person muted the app's notices, or every app's.</summary>
    /// <param name="userId">The person.</param>
    /// <param name="clientId">The app.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<bool> IsMutedAsync(Guid userId, string clientId, CancellationToken cancellationToken = default);

    /// <summary>Mutes the app's notices for the person; muting twice changes nothing.</summary>
    /// <param name="userId">The person.</param>
    /// <param name="clientId">The app, or <see cref="IdentityNoticeMute.AllClients"/>.</param>
    /// <param name="at">The instant.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task MuteAsync(Guid userId, string clientId, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>Lifts every mute of the people.</summary>
    /// <param name="userIds">The people.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>How many mutes were lifted.</returns>
    Task<int> UnmuteAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);

    /// <summary>Removes a deleted account's requests and mutes.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task DeleteAccountAsync(Guid userId, CancellationToken cancellationToken = default);
}

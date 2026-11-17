using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>Removes a deleted account's app notice requests, mutes and kept notices' ledger rows; the notices stay in the chat.</summary>
/// <param name="store">The requests and mutes.</param>
/// <param name="ledger">The bot's message ledger.</param>
public sealed class AppNoticeDeletionHandler(IAppNoticeRepository store, ITelegramMessageLedger ledger) : IAccountDeletionHandler
{
    /// <inheritdoc />
    public async Task HandleAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        await store.DeleteAccountAsync(user.Id, cancellationToken);
        await ledger.RemoveKeptAsync(user.Id, cancellationToken);
    }
}

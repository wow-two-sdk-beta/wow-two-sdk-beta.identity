namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>
/// Sends the bot's notices: a text about an account, with optional inline buttons, that cleans itself up. A sent notice
/// makes the account's older notices of the same kind in the chat due at once and is itself due after
/// <see cref="TelegramBotOptions.NoticeLifetime"/>; the sweep (<see cref="TelegramMessageSweeper"/>) deletes due ones.
/// </summary>
public interface ITelegramNoticeSender
{
    /// <summary>Sends <paramref name="notice"/>; a refusal from Telegram is returned, not thrown.</summary>
    /// <param name="notice">The notice.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<TelegramNoticeResult> SendAsync(TelegramNotice notice, CancellationToken cancellationToken = default);
}

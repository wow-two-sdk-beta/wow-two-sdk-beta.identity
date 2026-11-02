namespace WoW.Two.Sdk.Beta.Be.Identity.Otp.Telegram;

/// <summary>
/// Records each code message <see cref="TelegramOtpDeliveryHandler"/> sent, so it can be cleaned up once the code expires.
/// The instance's own bot (<c>AddTelegramLinks</c>) registers one over its message ledger; a host with its own bot and
/// none registered records nothing. A recorder never throws for a failed write: the code has reached the chat already.
/// </summary>
public interface ITelegramCodeMessageRecorder
{
    /// <summary>Records a sent code message, due for cleanup when the code expires.</summary>
    /// <param name="chatId">The chat the code went to.</param>
    /// <param name="messageId">Telegram's id of the message in that chat.</param>
    /// <param name="lifetime">How long the code stays valid from now.</param>
    /// <param name="culture">The recipient's culture name, such as <c>ru</c>; null when unknown.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task RecordAsync(long chatId, int messageId, TimeSpan lifetime, string? culture, CancellationToken cancellationToken = default);
}

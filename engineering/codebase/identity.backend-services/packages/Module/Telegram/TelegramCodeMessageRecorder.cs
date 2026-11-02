using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Otp.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>
/// Records the <c>telegram</c> channel's code messages in the bot's ledger as <see cref="TelegramMessageKindConstants.Code"/>,
/// due when the code expires. A failed write is logged, never thrown: the code has reached the chat, and the message only
/// stays there.
/// </summary>
/// <param name="ledger">The bot's message ledger.</param>
/// <param name="options">The bot; <c>Identity:Telegram</c> reloads live.</param>
/// <param name="time">The clock the code's expiry is measured from.</param>
/// <param name="logger">Logs a failed write.</param>
public sealed partial class TelegramCodeMessageRecorder(
    ITelegramMessageLedger ledger,
    IOptionsMonitor<TelegramBotOptions> options,
    TimeProvider time,
    ILogger<TelegramCodeMessageRecorder> logger) : ITelegramCodeMessageRecorder
{
    /// <summary>The longest culture name the ledger keeps; a longer one falls back to the default culture.</summary>
    internal const int MaxCultureLength = 16;

    /// <inheritdoc />
    public async Task RecordAsync(long chatId, int messageId, TimeSpan lifetime, string? culture, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        try
        {
            await ledger.AddAsync(
                new IdentityTelegramMessage
                {
                    Id = Guid.CreateVersion7(now),
                    BotUserName = options.CurrentValue.NormalizedBotUserName,
                    ChatId = chatId,
                    MessageId = messageId,
                    Kind = TelegramMessageKindConstants.Code,
                    Culture = string.IsNullOrWhiteSpace(culture) || culture.Length > MaxCultureLength ? null : culture,
                    SentAt = now,
                    DeleteAt = now + lifetime,
                },
                [],
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogRecordFailed(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording a Telegram code message for cleanup failed; it stays in its chat.")]
    private static partial void LogRecordFailed(ILogger logger, Exception exception);
}

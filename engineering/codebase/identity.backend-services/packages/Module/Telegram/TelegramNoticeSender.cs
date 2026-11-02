using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.ReplyMarkups;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>
/// Sends the bot's notices and records them in its ledger. Each button's <c>callback_data</c> is 32 random bytes in
/// base64url (43 characters, inside Telegram's 64), stored only as its SHA-256; the action, payload and allowed Telegram
/// user stay in the ledger, never in Telegram.
/// </summary>
/// <param name="bot">The bot client.</param>
/// <param name="ledger">The bot's message ledger.</param>
/// <param name="options">The bot; <c>Identity:Telegram</c> reloads live.</param>
/// <param name="time">The clock a notice's lifetime is measured from.</param>
/// <param name="logger">Logs notices Telegram refused.</param>
public sealed partial class TelegramNoticeSender(
    ITelegramBotClient bot,
    ITelegramMessageLedger ledger,
    IOptionsMonitor<TelegramBotOptions> options,
    TimeProvider time,
    ILogger<TelegramNoticeSender> logger) : ITelegramNoticeSender
{
    private const int TokenBytes = 32;
    private const int MaxNameLength = 64;
    private const int MaxPayloadLength = 256;

    /// <inheritdoc />
    public async Task<TelegramNoticeResult> SendAsync(TelegramNotice notice, CancellationToken cancellationToken = default)
    {
        Validate(notice);
        var current = options.CurrentValue;
        if (!current.IsEnabled)
            return new TelegramNoticeResult { FailureReason = "bot_disabled" };

        var tokens = notice.Buttons.Select(_ => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes))).ToList();
        var keyboard = notice.Buttons.Count == 0
            ? null
            : new InlineKeyboardMarkup(notice.Buttons.Select((button, index) => new[] { InlineKeyboardButton.WithCallbackData(button.Label, tokens[index]) }));

        int messageId;
        try
        {
            messageId = (await bot.SendMessage(notice.ChatId, notice.Text, replyMarkup: keyboard, cancellationToken: cancellationToken)).Id;
        }
        catch (RequestException exception)
        {
            LogRefused(logger, notice.Kind, exception.Message);
            return new TelegramNoticeResult { FailureReason = exception.Message };
        }

        var now = time.GetUtcNow();
        var botUserName = current.NormalizedBotUserName;
        await ledger.MakeDueAsync(botUserName, notice.ChatId, notice.Kind, notice.UserId, now, cancellationToken);

        var record = new IdentityTelegramMessage
        {
            Id = Guid.CreateVersion7(now),
            BotUserName = botUserName,
            ChatId = notice.ChatId,
            MessageId = messageId,
            Kind = notice.Kind,
            UserId = notice.UserId,
            Culture = string.IsNullOrWhiteSpace(notice.Culture) || notice.Culture.Length > TelegramCodeMessageRecorder.MaxCultureLength ? null : notice.Culture,
            SentAt = now,
            DeleteAt = now + current.NoticeLifetime,
        };
        var callbacks = notice.Buttons
            .Select((button, index) => new IdentityTelegramCallback
            {
                Id = Guid.CreateVersion7(now),
                MessageRecordId = record.Id,
                TokenHash = TokenHashOf(tokens[index]),
                Action = button.Action,
                Payload = button.Payload,
                AllowedTelegramUserId = button.AllowedTelegramUserId,
            })
            .ToList();
        await ledger.AddAsync(record, callbacks, cancellationToken);
        return new TelegramNoticeResult { IsSent = true, MessageId = messageId };
    }

    /// <summary>The hex SHA-256 a button's callback data is stored and looked up by.</summary>
    /// <param name="token">The callback data.</param>
    internal static string TokenHashOf(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static void Validate(TelegramNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        ArgumentException.ThrowIfNullOrWhiteSpace(notice.Kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(notice.Text);
        ArgumentNullException.ThrowIfNull(notice.Buttons);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(notice.Kind.Length, MaxNameLength, nameof(notice));
        foreach (var button in notice.Buttons)
        {
            ArgumentNullException.ThrowIfNull(button, nameof(notice));
            ArgumentException.ThrowIfNullOrWhiteSpace(button.Label, nameof(notice));
            ArgumentException.ThrowIfNullOrWhiteSpace(button.Action, nameof(notice));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(button.Action.Length, MaxNameLength, nameof(notice));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(button.Payload?.Length ?? 0, MaxPayloadLength, nameof(notice));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram refused the bot's '{Kind}' notice: {Reason}")]
    private static partial void LogRefused(ILogger logger, string kind, string reason);
}

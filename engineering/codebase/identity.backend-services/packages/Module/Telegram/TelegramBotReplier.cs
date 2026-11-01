using System.Globalization;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.ReplyMarkups;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>Sends the bot's own texts in the person's culture; a refused reply is logged, never thrown, so Telegram never retries an update.</summary>
/// <param name="bot">The bot client.</param>
/// <param name="messages">The bot's wording.</param>
/// <param name="logger">Logs replies Telegram refused.</param>
public sealed partial class TelegramBotReplier(ITelegramBotClient bot, TelegramBotMessages messages, ILogger<TelegramBotReplier> logger)
{
    /// <summary>Sends the text for <paramref name="key"/> to <paramref name="chatId"/>.</summary>
    /// <param name="chatId">The chat.</param>
    /// <param name="key">One of <see cref="TelegramBotMessageKeyConstants"/>.</param>
    /// <param name="culture">The person's culture.</param>
    /// <param name="keyboard">The reply keyboard to show or remove; null leaves it.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task ReplyAsync(long chatId, string key, CultureInfo culture, ReplyMarkup? keyboard = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await bot.SendMessage(chatId, messages.Text(key, culture), replyMarkup: keyboard, cancellationToken: cancellationToken);
        }
        catch (RequestException exception)
        {
            LogReplyRefused(logger, key, exception.Message);
        }
    }

    /// <summary>The keyboard with one button that shares the person's own contact.</summary>
    /// <param name="culture">The person's culture.</param>
    public ReplyKeyboardMarkup ShareKeyboard(CultureInfo culture)
        => new(new KeyboardButton(messages.Text(TelegramBotMessageKeyConstants.ShareButton, culture)) { RequestContact = true })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true,
        };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram refused the bot's '{Key}' reply: {Reason}")]
    private static partial void LogReplyRefused(ILogger logger, string key, string reason);
}

using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>
/// Routes a press of a notice's inline button (<c>callback_query</c>): it finds the button by its token's SHA-256, refuses an
/// unknown or spent one and a presser other than the button's allowed Telegram user (<c>from.id</c>), spends the button and
/// its siblings, and dispatches to the <see cref="ITelegramCallbackHandler"/> keyed by the button's action. The notice then
/// loses its buttons, taking the handler's new text when it gives one. Every query is answered
/// (<c>answerCallbackQuery</c>), refused, failed or handled, so the presser's client stops waiting.
/// </summary>
/// <remarks>
///   - a refused press by another user leaves the button usable for its own user
///   - a handler that throws is logged and answered with <see cref="TelegramBotMessageKeyConstants.ButtonFailed"/>, never
///     rethrown: the button is spent, so a retried update would only be refused
/// </remarks>
/// <param name="bot">The bot client.</param>
/// <param name="ledger">The recorded messages and buttons.</param>
/// <param name="messages">The bot's wording.</param>
/// <param name="options">The bot; <c>Identity:Telegram</c> reloads live.</param>
/// <param name="time">The clock a press is stamped with.</param>
/// <param name="services">The scope's provider, for the handler keyed by the action.</param>
/// <param name="logger">Logs presses no handler serves, failed handlers and refused edits or answers.</param>
public sealed partial class TelegramCallbackRouter(
    ITelegramBotClient bot,
    ITelegramMessageLedger ledger,
    TelegramBotMessages messages,
    IOptionsMonitor<TelegramBotOptions> options,
    TimeProvider time,
    IServiceProvider services,
    ILogger<TelegramCallbackRouter> logger)
{
    /// <summary>Acts on one button press and answers it.</summary>
    /// <param name="query">The callback query Telegram posted.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task RouteAsync(CallbackQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var culture = messages.CultureOf(query.From?.LanguageCode);
        string? answer = null;
        try
        {
            answer = await DecideAsync(query, culture, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPressFailed(logger, exception);
            answer = messages.Text(TelegramBotMessageKeyConstants.ButtonFailed, culture);
        }
        finally
        {
            await AnswerAsync(query.Id, answer, cancellationToken);
        }
    }

    /// <summary>Checks and spends the button, runs its handler and edits the notice; returns the toast for the presser.</summary>
    private async Task<string?> DecideAsync(CallbackQuery query, CultureInfo culture, CancellationToken cancellationToken)
    {
        var data = query.Data;
        if (string.IsNullOrEmpty(data) || Encoding.UTF8.GetByteCount(data) > TelegramBotLimits.MaxCallbackDataBytes || query.From is null)
            return messages.Text(TelegramBotMessageKeyConstants.ButtonExpired, culture);

        var found = await ledger.FindCallbackAsync(TelegramNoticeSender.TokenHashOf(data), cancellationToken);
        if (found is null
            || found.Callback.UsedAt is not null
            || !string.Equals(found.Message.BotUserName, options.CurrentValue.NormalizedBotUserName, StringComparison.Ordinal))
            return messages.Text(TelegramBotMessageKeyConstants.ButtonExpired, culture);

        if (found.Callback.AllowedTelegramUserId != query.From.Id)
            return messages.Text(TelegramBotMessageKeyConstants.ButtonNotYours, culture);

        if (!await ledger.TryUseCallbackAsync(found.Callback.Id, found.Message.Id, time.GetUtcNow(), cancellationToken))
            return messages.Text(TelegramBotMessageKeyConstants.ButtonExpired, culture);

        var handler = services.GetKeyedService<ITelegramCallbackHandler>(found.Callback.Action);
        if (handler is null)
        {
            LogNoHandler(logger, found.Callback.Action);
            await EditAsync(found.Message, newText: null, cancellationToken);
            return messages.Text(TelegramBotMessageKeyConstants.ButtonExpired, culture);
        }

        var result = await handler.HandleAsync(
            new TelegramCallback
            {
                Action = found.Callback.Action,
                Payload = found.Callback.Payload,
                Kind = found.Message.Kind,
                UserId = found.Message.UserId,
                ChatId = found.Message.ChatId,
                MessageId = found.Message.MessageId,
                TelegramUserId = query.From.Id,
                Culture = culture,
            },
            cancellationToken) ?? TelegramCallbackResult.Done;
        await EditAsync(found.Message, result.MessageText, cancellationToken);
        return result.AnswerText;
    }

    /// <summary>Drops the notice's buttons, replacing its text when <paramref name="newText"/> is given; a refusal is logged.</summary>
    private async Task EditAsync(IdentityTelegramMessage message, string? newText, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(newText))
                await bot.EditMessageReplyMarkup(message.ChatId, message.MessageId, replyMarkup: null, cancellationToken: cancellationToken);
            else
                await bot.EditMessageText(message.ChatId, message.MessageId, newText, replyMarkup: null, cancellationToken: cancellationToken);
        }
        catch (RequestException exception)
        {
            LogEditRefused(logger, message.Kind, exception.Message);
        }
    }

    private async Task AnswerAsync(string queryId, string? text, CancellationToken cancellationToken)
    {
        try
        {
            await bot.AnswerCallbackQuery(queryId, text, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is RequestException or OperationCanceledException)
        {
            LogAnswerRefused(logger, exception.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A Telegram button for the action '{Action}' was pressed, but no handler serves that action.")]
    private static partial void LogNoHandler(ILogger logger, string action);

    [LoggerMessage(Level = LogLevel.Error, Message = "A Telegram button press failed; the button is spent.")]
    private static partial void LogPressFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram refused to edit the bot's '{Kind}' message after a button press: {Reason}")]
    private static partial void LogEditRefused(ILogger logger, string kind, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram refused the answer to a button press: {Reason}")]
    private static partial void LogAnswerRefused(ILogger logger, string reason);
}

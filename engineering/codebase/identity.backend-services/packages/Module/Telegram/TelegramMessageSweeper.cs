using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>
/// Cleans up the bot's due messages once: those sent under 48 hours ago are deleted with <c>deleteMessages</c>, per chat and
/// at most 100 ids a call; those 48 hours or older, which Telegram no longer lets the bot delete, are edited to the one-line
/// <see cref="TelegramBotMessageKeyConstants.MessageExpired"/> stub with no buttons, in the message's culture. A cleaned-up
/// row leaves the ledger with its buttons. <see cref="TelegramMessageSweepService"/> runs it each minute.
/// </summary>
/// <remarks>
///   - a message Telegram no longer finds, or that already reads as the stub, counts as done
///   - a deletion Telegram refuses (<c>400</c>, such as a message just past 48 hours) falls back to the stub
///   - a chat that refuses the bot (<c>403</c>: blocked or left) counts as done, logged, since nothing in it can change
///   - any other failure (network, <c>429</c>, <c>5xx</c>) is logged and its rows stay for the next sweep
///   - only the current bot's rows are touched; another bot's messages are beyond this token
/// </remarks>
/// <param name="ledger">The recorded messages.</param>
/// <param name="services">The scope's provider, for the bot client, resolved only when something is due.</param>
/// <param name="messages">The stub's wording.</param>
/// <param name="options">The bot; <c>Identity:Telegram</c> reloads live.</param>
/// <param name="schedule">The batch size.</param>
/// <param name="time">The clock dueness and age are judged against.</param>
/// <param name="logger">Logs failed and given-up cleanups.</param>
public sealed partial class TelegramMessageSweeper(
    ITelegramMessageLedger ledger,
    IServiceProvider services,
    TelegramBotMessages messages,
    IOptionsMonitor<TelegramBotOptions> options,
    TelegramMessageSweepSchedule schedule,
    TimeProvider time,
    ILogger<TelegramMessageSweeper> logger)
{
    /// <summary>Cleans up the messages due now, up to <see cref="TelegramMessageSweepSchedule.BatchLimit"/>.</summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>How many messages were cleaned up and left the ledger.</returns>
    public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var due = await ledger.FindDueAsync(options.CurrentValue.NormalizedBotUserName, now, schedule.BatchLimit, cancellationToken);
        if (due.Count == 0)
            return 0;

        var bot = services.GetRequiredService<ITelegramBotClient>();
        var done = new List<Guid>();
        var deletableSince = now - TelegramBotLimits.DeleteWindow;
        var stubs = due.Where(m => m.SentAt <= deletableSince).ToList();
        foreach (var chat in due.Where(m => m.SentAt > deletableSince).GroupBy(m => m.ChatId))
        {
            foreach (var chunk in chat.Chunk(TelegramBotLimits.MaxIdsPerDelete))
                await DeleteAsync(bot, chat.Key, chunk, done, stubs, cancellationToken);
        }

        foreach (var message in stubs)
        {
            if (!await StubAsync(bot, message, done, cancellationToken))
                break;
        }

        await ledger.RemoveAsync(done, cancellationToken);
        return done.Count;
    }

    private async Task DeleteAsync(ITelegramBotClient bot, long chatId, IdentityTelegramMessage[] chunk, List<Guid> done, List<IdentityTelegramMessage> stubs, CancellationToken cancellationToken)
    {
        try
        {
            await bot.DeleteMessages(chatId, chunk.Select(m => m.MessageId), cancellationToken);
            done.AddRange(chunk.Select(m => m.Id));
        }
        catch (ApiRequestException exception) when (IsGone(exception))
        {
            done.AddRange(chunk.Select(m => m.Id));
        }
        catch (ApiRequestException exception) when (exception.ErrorCode == 403)
        {
            LogChatRefused(logger, chunk.Length, exception.Message);
            done.AddRange(chunk.Select(m => m.Id));
        }
        catch (ApiRequestException exception) when (exception.ErrorCode == 400)
        {
            // Telegram would not delete them (most likely just past 48 hours): they become stubs instead.
            stubs.AddRange(chunk);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogDeleteFailed(logger, chunk.Length, exception.Message);
        }
    }

    /// <summary>Edits one message to the stub; returns false when Telegram asks to slow down, which ends this sweep's edits.</summary>
    private async Task<bool> StubAsync(ITelegramBotClient bot, IdentityTelegramMessage message, List<Guid> done, CancellationToken cancellationToken)
    {
        try
        {
            await bot.EditMessageText(message.ChatId, message.MessageId, StubText(message), replyMarkup: null, cancellationToken: cancellationToken);
            done.Add(message.Id);
        }
        catch (ApiRequestException exception) when (IsGone(exception) || exception.Message.Contains("not modified", StringComparison.OrdinalIgnoreCase))
        {
            done.Add(message.Id);
        }
        catch (ApiRequestException exception) when (exception.ErrorCode is 400 or 403)
        {
            LogStubRefused(logger, message.Kind, exception.Message);
            done.Add(message.Id);
        }
        catch (ApiRequestException exception) when (exception.ErrorCode == 429)
        {
            LogStubFailed(logger, message.Kind, exception.Message);
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogStubFailed(logger, message.Kind, exception.Message);
        }

        return true;
    }

    private string StubText(IdentityTelegramMessage message)
    {
        var culture = messages.CultureOf(message.Culture);
        return messages.Text(TelegramBotMessageKeyConstants.MessageExpired, culture);
    }

    /// <summary>Whether Telegram reports the message or its chat gone, so there is nothing left to clean up.</summary>
    private static bool IsGone(ApiRequestException exception)
        => exception.ErrorCode == 400 && exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deleting {Count} Telegram messages failed: {Reason}. They stay due for the next sweep.")]
    private static partial void LogDeleteFailed(ILogger logger, int count, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A chat refused the bot while deleting {Count} of its messages: {Reason}. They are left as they are.")]
    private static partial void LogChatRefused(ILogger logger, int count, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram refused to turn the bot's '{Kind}' message into its stub: {Reason}. It is left as it is.")]
    private static partial void LogStubRefused(ILogger logger, string kind, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Turning the bot's '{Kind}' message into its stub failed: {Reason}. It stays due for the next sweep.")]
    private static partial void LogStubFailed(ILogger logger, string kind, string reason);
}

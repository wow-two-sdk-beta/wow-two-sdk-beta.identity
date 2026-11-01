using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>
/// Reads the bot's updates by long polling <c>getUpdates</c> for a local run, which has no public address for a webhook, and
/// hands each to the <see cref="TelegramUpdateHandler"/> the webhook uses. It runs only while
/// <see cref="TelegramBotOptions.UpdateMode"/> is <see cref="TelegramUpdateMode.Polling"/> at start, and polls only while
/// <see cref="TelegramBotOptions.IsEnabled"/> is on.
/// </summary>
/// <remarks>
///   - it never calls <c>deleteWebhook</c>: a bot with a webhook set belongs to a hosted instance, so Telegram answers
///     <c>409</c>, which logs one error naming the cure (the local run's own bot, or test mode) and backs off
///   - an update that throws is logged and skipped, so one poisoned update cannot stop the bot; the webhook, by contrast, answers
///     <c>500</c> and lets Telegram retry
///   - the bot client is resolved when polling starts, so an instance with the bot off never needs a token
/// </remarks>
/// <param name="services">The root provider, for the bot client and one scope per update.</param>
/// <param name="options">The bot; <c>Identity:Telegram</c> reloads live, the mode only at start.</param>
/// <param name="schedule">The pacing.</param>
/// <param name="logger">Logs the mode, refused polls and failed updates.</param>
internal sealed partial class TelegramPollingService(
    IServiceProvider services,
    IOptionsMonitor<TelegramBotOptions> options,
    TelegramPollingSchedule schedule,
    ILogger<TelegramPollingService> logger) : BackgroundService
{
    private static readonly UpdateType[] AllowedUpdates = [UpdateType.Message];

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.CurrentValue.UpdateMode != TelegramUpdateMode.Polling)
            return;

        // Let the host finish starting before the first poll.
        await Task.Yield();
        ITelegramBotClient? bot = null;
        int? offset = null;
        var failures = 0;
        var isRefusalReported = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!options.CurrentValue.IsEnabled)
                {
                    await Task.Delay(schedule.IdleDelay, stoppingToken);
                    continue;
                }

                if (bot is null)
                {
                    bot = services.GetRequiredService<ITelegramBotClient>();
                    LogPolling(logger, options.CurrentValue.NormalizedBotUserName);
                }

                var updates = await bot.GetUpdates(offset, schedule.BatchLimit, schedule.PollTimeoutSeconds, AllowedUpdates, stoppingToken);
                failures = 0;
                isRefusalReported = false;
                foreach (var update in updates)
                {
                    await DispatchAsync(update, stoppingToken);
                    offset = update.Id + 1;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                failures++;
                var delay = schedule.BackoffAfter(failures);
                if (exception is ApiRequestException { ErrorCode: 409 } conflict)
                {
                    if (!isRefusalReported)
                        ReportConflict(conflict, delay);
                    isRefusalReported = true;
                }
                else
                {
                    LogPollFailed(logger, exception.Message, delay);
                }

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>Hands one update to the handler in its own scope; a failure is logged and the update skipped.</summary>
    private async Task DispatchAsync(Update update, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<TelegramUpdateHandler>().HandleAsync(update, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogUpdateFailed(logger, update.Id, exception);
        }
    }

    private void ReportConflict(ApiRequestException conflict, TimeSpan delay)
    {
        if (conflict.Message.Contains("webhook", StringComparison.OrdinalIgnoreCase))
            LogWebhookSet(logger, options.CurrentValue.NormalizedBotUserName, delay);
        else
            LogAnotherPoller(logger, conflict.Message, delay);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Reading the Telegram bot '{BotUserName}' by polling getUpdates.")]
    private static partial void LogPolling(ILogger logger, string botUserName);

    [LoggerMessage(
        EventId = 7301,
        Level = LogLevel.Error,
        Message = "Telegram refused getUpdates with 409 because a webhook is set for the bot '{BotUserName}': it belongs to a hosted instance, and this local run never removes that webhook. Give this run its own bot (Identity:Telegram:BotToken and BotUserName of a second bot) or turn on Identity:TestMode:IsEnabled. Retrying in {Delay}, without logging again until a poll succeeds.")]
    private static partial void LogWebhookSet(ILogger logger, string botUserName, TimeSpan delay);

    [LoggerMessage(
        EventId = 7302,
        Level = LogLevel.Error,
        Message = "Telegram refused getUpdates with 409: {Reason}. Another instance is polling this bot; stop it or give this run its own bot. Retrying in {Delay}, without logging again until a poll succeeds.")]
    private static partial void LogAnotherPoller(ILogger logger, string reason, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Polling the Telegram bot failed: {Reason}. Retrying in {Delay}.")]
    private static partial void LogPollFailed(ILogger logger, string reason, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Error, Message = "Telegram update {UpdateId} failed and was skipped.")]
    private static partial void LogUpdateFailed(ILogger logger, int updateId, Exception exception);
}

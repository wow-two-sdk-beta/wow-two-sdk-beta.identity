using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>
/// Runs the <see cref="TelegramMessageSweeper"/> once per <see cref="TelegramMessageSweepSchedule.Interval"/>, in its own
/// scope, while the bot is on, in either update mode. A failed sweep is logged and the next one runs on time: the sweep
/// never stops the host. Instances sharing a database may sweep the same rows; a repeated delete or edit counts as done.
/// </summary>
/// <param name="services">The root provider, for one scope per sweep.</param>
/// <param name="options">The bot; <c>Identity:Telegram</c> reloads live.</param>
/// <param name="schedule">The pacing.</param>
/// <param name="logger">Logs failed sweeps.</param>
internal sealed partial class TelegramMessageSweepService(
    IServiceProvider services,
    IOptionsMonitor<TelegramBotOptions> options,
    TelegramMessageSweepSchedule schedule,
    ILogger<TelegramMessageSweepService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the host finish starting; the first sweep waits one interval.
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(schedule.Interval, stoppingToken);
                if (!options.CurrentValue.IsEnabled)
                    continue;

                await using var scope = services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<TelegramMessageSweeper>().SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogSweepFailed(logger, exception.Message, exception);
            }
        }
    }

    [LoggerMessage(EventId = 7304, Level = LogLevel.Error, Message = "Sweeping the Telegram bot's messages failed: {Reason}. The next sweep runs on time.")]
    private static partial void LogSweepFailed(ILogger logger, string reason, Exception exception);
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;

/// <summary>
/// Deletes sign-in log rows past the retention at startup and then every <see cref="SignInLogOptions.CleanupInterval"/>.
/// Each run is one idempotent <c>DELETE … WHERE occurred_at &lt; cut-off</c>, so several replicas may run it at once.
/// </summary>
/// <param name="services">The root provider; each run opens its own scope.</param>
/// <param name="options">The interval.</param>
/// <param name="time">The clock the interval runs on.</param>
/// <param name="logger">Receives each run's count and failures.</param>
internal sealed partial class SignInLogCleanupService(
    IServiceProvider services,
    SignInLogOptions options,
    TimeProvider time,
    ILogger<SignInLogCleanupService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.CleanupInterval, time);
        do
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var deleted = await scope.ServiceProvider.GetRequiredService<SignInLogService>().DeleteExpiredAsync(stoppingToken);
                if (deleted > 0)
                    LogDeleted(logger, deleted, options.RetentionDays);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(EventId = 7302, Level = LogLevel.Information, Message = "Sign-in log cleanup deleted {Count} row(s) older than {RetentionDays} days")]
    private static partial void LogDeleted(ILogger logger, int count, int retentionDays);

    [LoggerMessage(EventId = 7303, Level = LogLevel.Warning, Message = "Sign-in log cleanup failed; the next run retries")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

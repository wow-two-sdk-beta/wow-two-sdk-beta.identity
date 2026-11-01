using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>
/// Registers the bot's webhook with Telegram when a deployed instance starts: it reads <c>getWebhookInfo</c> and calls
/// <c>setWebhook</c> only when the address or the allowed updates differ, or Telegram's last delivery was refused with
/// <c>401</c>. It does nothing on a local run, in polling mode, while the bot is off, or with no public address.
/// </summary>
/// <remarks>
///   - the address is <see cref="TelegramBotOptions.WebhookUrl"/> (the service host fills it from <c>Identity:Server:Issuer</c>),
///     the <c>secret_token</c> is <see cref="TelegramBotOptions.WebhookSecret"/> and the allowed updates are <c>message</c>
///   - Telegram never returns the <c>secret_token</c>, so a changed secret cannot be compared: the one signal is the
///     <c>401</c> Telegram records when the instance rejects its deliveries, which re-registers on the next start
///   - it runs in the background, so a slow or failing Telegram never delays or stops the start; a failure is logged
/// </remarks>
/// <param name="services">The root provider: the bot client, resolved only when a registration is due, and the host environment and configuration that tell a local run from a deployed boot, when there is a host.</param>
/// <param name="options">The bot.</param>
/// <param name="logger">Logs what was registered or why not.</param>
internal sealed partial class TelegramWebhookRegistrationService(
    IServiceProvider services,
    IOptionsMonitor<TelegramBotOptions> options,
    ILogger<TelegramWebhookRegistrationService> logger) : BackgroundService
{
    /// <summary>The update types the instance asks Telegram for.</summary>
    internal static readonly UpdateType[] AllowedUpdates = [UpdateType.Message];

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var current = options.CurrentValue;
        if (!current.IsEnabled || current.UpdateMode != TelegramUpdateMode.Webhook || !IsDeployedBoot())
            return;

        // Let the host finish starting; a registration is never part of it.
        await Task.Yield();
        try
        {
            await RegisterAsync(current, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, exception.Message, exception);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Whether this is a deployed boot; a container with no host environment is not known to be one.</summary>
    private bool IsDeployedBoot()
        => services.GetService<IHostEnvironment>() is { } environment
            && services.GetService<IConfiguration>() is { } configuration
            && !environment.IsLocalRun(configuration);

    private async Task RegisterAsync(TelegramBotOptions current, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(current.WebhookUrl))
        {
            LogNoAddress(logger);
            return;
        }

        var bot = services.GetRequiredService<ITelegramBotClient>();
        var info = await bot.GetWebhookInfo(cancellationToken);
        if (IsRegistered(info, current.WebhookUrl))
        {
            LogCurrent(logger, current.WebhookUrl);
            return;
        }

        await bot.SetWebhook(current.WebhookUrl, secretToken: current.WebhookSecret, allowedUpdates: AllowedUpdates, cancellationToken: cancellationToken);
        LogRegistered(logger, current.WebhookUrl);
    }

    /// <summary>Whether Telegram already delivers to <paramref name="url"/> for exactly the allowed updates, and has not been refused by the instance.</summary>
    /// <param name="info">What <c>getWebhookInfo</c> reported.</param>
    /// <param name="url">The wanted address.</param>
    internal static bool IsRegistered(WebhookInfo info, string url)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (!string.Equals(info.Url, url, StringComparison.Ordinal))
            return false;

        var allowed = info.AllowedUpdates ?? [];
        if (allowed.Length != AllowedUpdates.Length || !AllowedUpdates.All(allowed.Contains))
            return false;

        return info.LastErrorMessage is null || !info.LastErrorMessage.Contains("401", StringComparison.Ordinal);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The Telegram webhook already points at {Url}.")]
    private static partial void LogCurrent(ILogger logger, string url);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered the Telegram webhook at {Url}.")]
    private static partial void LogRegistered(ILogger logger, string url);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No public address for the Telegram webhook: set Identity:Server:Issuer or Identity:Telegram:WebhookUrl. Telegram keeps whatever it has.")]
    private static partial void LogNoAddress(ILogger logger);

    [LoggerMessage(EventId = 7303, Level = LogLevel.Error, Message = "Registering the Telegram webhook failed: {Reason}. The instance runs; the bot reaches it only if a webhook is already set.")]
    private static partial void LogFailed(ILogger logger, string reason, Exception exception);
}

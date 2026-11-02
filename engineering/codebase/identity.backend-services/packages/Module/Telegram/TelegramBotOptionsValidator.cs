using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>
/// Checks the bot at boot, failing closed while it is on: a token of <c>{bot id}:{secret}</c>, a user name Telegram
/// allows, a known update mode, a webhook secret Telegram accepts (required in webhook mode, checked when given in
/// polling mode), a webhook address that is an absolute HTTPS URL when given, a positive link lifetime, and on a deployed
/// boot a configured <c>Identity:Otp:HashKey</c>, which every instance needs to verify the codes another one made, and
/// the webhook mode, because a deployed instance has a public address and a local run's polling would take its updates,
/// and a notice lifetime that is positive and below 48 hours, so the sweep can still delete a notice.
/// </summary>
/// <param name="services">The root provider, to read the OTP options and tell a local run from a deployed boot.</param>
internal sealed partial class TelegramBotOptionsValidator(IServiceProvider services) : IValidateOptions<TelegramBotOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TelegramBotOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsEnabled)
            return ValidateOptionsResult.Success;

        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(options.BotToken) || !TokenPattern().IsMatch(options.BotToken))
            problems.Add($"{TelegramBotOptions.SectionName}:{nameof(options.BotToken)} is required while the bot is on, as '{{bot id}}:{{secret}}'.");
        if (string.IsNullOrWhiteSpace(options.BotUserName) || !UserNamePattern().IsMatch(options.BotUserName.Trim().TrimStart('@')))
            problems.Add($"{TelegramBotOptions.SectionName}:{nameof(options.BotUserName)} is required while the bot is on: 5–32 letters, digits or underscores.");
        if (!Enum.IsDefined(options.UpdateMode))
            problems.Add($"{TelegramBotOptions.SectionName}:{nameof(options.UpdateMode)} must be {nameof(TelegramUpdateMode.Webhook)} or {nameof(TelegramUpdateMode.Polling)}.");
        else if (options.UpdateMode == TelegramUpdateMode.Polling && !IsLocalRun())
            problems.Add($"{TelegramBotOptions.SectionName}:{nameof(options.UpdateMode)} {nameof(TelegramUpdateMode.Polling)} is allowed only on a local run (Development without DEPLOY_ENVIRONMENT); a deployed boot reads the bot by {nameof(TelegramUpdateMode.Webhook)}.");

        var isSecretGiven = !string.IsNullOrEmpty(options.WebhookSecret);
        if (options.UpdateMode != TelegramUpdateMode.Polling && !isSecretGiven)
            problems.Add($"{TelegramBotOptions.SectionName}:{nameof(options.WebhookSecret)} is required while the bot is on: 1–256 of A-Z, a-z, 0-9, _ and -.");
        else if (isSecretGiven && !SecretPattern().IsMatch(options.WebhookSecret!))
            problems.Add($"{TelegramBotOptions.SectionName}:{nameof(options.WebhookSecret)} must be 1–256 of A-Z, a-z, 0-9, _ and -.");
        if (!string.IsNullOrWhiteSpace(options.WebhookUrl)
            && !(Uri.TryCreate(options.WebhookUrl, UriKind.Absolute, out var webhook) && webhook.Scheme == Uri.UriSchemeHttps))
            problems.Add($"{TelegramBotOptions.SectionName}:{nameof(options.WebhookUrl)} must be an absolute https URL.");
        if (options.LinkLifetime <= TimeSpan.Zero)
            problems.Add($"{TelegramBotOptions.SectionName}:{nameof(options.LinkLifetime)} must be positive.");
        if (options.NoticeLifetime <= TimeSpan.Zero || options.NoticeLifetime >= TelegramBotLimits.DeleteWindow)
            problems.Add($"{TelegramBotOptions.SectionName}:{nameof(options.NoticeLifetime)} must be positive and below 48 hours, while Telegram still lets the bot delete a notice.");
        if (!IsLocalRun() && string.IsNullOrWhiteSpace(services.GetService<OtpOptions>()?.HashKey))
            problems.Add($"{OtpOptions.SectionName}:{nameof(OtpOptions.HashKey)} is required on a deployed boot while the bot is on, so every instance verifies the same codes.");

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }

    private bool IsLocalRun()
        => services.GetService<IHostEnvironment>() is not { } environment
            || services.GetService<IConfiguration>() is not { } configuration
            || environment.IsLocalRun(configuration);

    [GeneratedRegex("^[0-9]+:[A-Za-z0-9_-]+$")]
    private static partial Regex TokenPattern();

    [GeneratedRegex("^[A-Za-z0-9_]{5,32}$")]
    private static partial Regex UserNamePattern();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,256}$")]
    private static partial Regex SecretPattern();
}

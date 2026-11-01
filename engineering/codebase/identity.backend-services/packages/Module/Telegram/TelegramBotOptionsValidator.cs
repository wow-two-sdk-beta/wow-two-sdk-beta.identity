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
/// allows, a webhook secret Telegram accepts, a positive link lifetime, and on a deployed boot a configured
/// <c>Identity:Otp:HashKey</c>, which every instance needs to verify the codes another one made.
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
        if (string.IsNullOrEmpty(options.WebhookSecret) || !SecretPattern().IsMatch(options.WebhookSecret))
            problems.Add($"{TelegramBotOptions.SectionName}:{nameof(options.WebhookSecret)} is required while the bot is on: 1–256 of A-Z, a-z, 0-9, _ and -.");
        if (options.LinkLifetime <= TimeSpan.Zero)
            problems.Add($"{TelegramBotOptions.SectionName}:{nameof(options.LinkLifetime)} must be positive.");
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

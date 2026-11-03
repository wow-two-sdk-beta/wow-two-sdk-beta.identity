using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>
/// Checks trusted contacts at boot and in <c>config check</c>: while they are on, the instance's Telegram bot must be
/// registered and on, since every code a contact receives travels through it. <see cref="TrustedContactOptions.MaxPerPerson"/>'s
/// range is a named rule of the record itself.
/// </summary>
/// <param name="services">The root provider, to read the bot's options.</param>
internal sealed class TrustedContactOptionsValidator(IServiceProvider services) : IValidateOptions<TrustedContactOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TrustedContactOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsEnabled)
            return ValidateOptionsResult.Success;

        bool isBotOn;
        try
        {
            isBotOn = services.GetService<IOptionsMonitor<TelegramBotOptions>>()?.CurrentValue.IsEnabled == true;
        }
        catch (OptionsValidationException)
        {
            // The bot's own check reports its settings; this one only needs it on.
            isBotOn = true;
        }

        return isBotOn
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{TrustedContactOptions.SectionName}:{nameof(TrustedContactOptions.IsEnabled)} needs the instance's Telegram bot: set {TelegramBotOptions.SectionName}:{nameof(TelegramBotOptions.IsEnabled)} too, or turn trusted contacts off.");
    }
}

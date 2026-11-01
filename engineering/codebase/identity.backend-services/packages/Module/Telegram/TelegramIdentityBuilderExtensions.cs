using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;
using WoW.Two.Sdk.Beta.Be.Identity.Otp.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>Opts the instance's own Telegram bot into an identity registration.</summary>
public static class TelegramIdentityBuilderExtensions
{
    /// <summary>
    /// Registers the bot: <see cref="TelegramBotOptions"/> from <paramref name="configure"/>, then the host section
    /// <c>Identity:Telegram</c>, checked at boot; the bot client over <see cref="TelegramBotOptions.BotToken"/> unless one is
    /// registered already; the <c>telegram</c> OTP channel; the links over <see cref="IdentityDbContext"/>, the start links
    /// and the webhook's <see cref="TelegramUpdateHandler"/>. Map the webhook with <c>MapTelegramWebhook</c>.
    /// </summary>
    /// <param name="builder">The identity builder.</param>
    /// <param name="configure">The switch, the bot, the webhook secret, the link lifetime and the wording.</param>
    public static IdentityBuilder AddTelegramLinks(this IdentityBuilder builder, Action<TelegramBotOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.AddModuleOptions(TelegramBotOptions.SectionName, configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TelegramBotOptions>, TelegramBotOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITelegramBotClient>(provider => new TelegramBotClient(
            provider.GetRequiredService<IOptionsMonitor<TelegramBotOptions>>().CurrentValue.BotToken
                ?? throw new InvalidOperationException($"{TelegramBotOptions.SectionName}:{nameof(TelegramBotOptions.BotToken)} is required to reach the bot.")));
        services.AddTelegramOtpDelivery();
        services.TryAddScoped<ITelegramLinkRepository, EfTelegramLinkRepository>();
        services.TryAddScoped<TelegramLinkService>();
        services.TryAddSingleton<TelegramBotMessages>();
        services.TryAddScoped<TelegramBotReplier>();
        services.TryAddScoped<TelegramUpdateHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAccountDeletionHandler, TelegramLinkDeletionHandler>());
        return builder;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;

/// <summary>Opts sign-in by phone with a code from the instance's Telegram bot into an identity registration.</summary>
public static class PhoneSignInIdentityBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="UserPhoneSignInService"/> over the phone slice (<c>AddPhoneNumbers</c>) and the bot
    /// (<c>AddTelegramLinks</c>), and sends the pending code when a phone sign-in's start link completes. It runs while
    /// <c>Identity:Telegram:IsEnabled</c> is on; map its routes with <c>MapPhoneSignInEndpoints</c> and the bot's with
    /// <c>MapTelegramWebhook</c>. Hosts with several instances add <c>AddDatabaseOtpStore</c> and one
    /// <c>Identity:Otp:HashKey</c>.
    /// </summary>
    /// <param name="builder">The identity builder.</param>
    /// <param name="telegram">The bot, from code; the host section <c>Identity:Telegram</c> is applied last.</param>
    /// <param name="phones">The allowed countries, from code; the host section <c>Identity:Phone</c> is applied last.</param>
    public static IdentityBuilder AddPhoneSignIn(this IdentityBuilder builder, Action<TelegramBotOptions>? telegram = null, Action<PhoneOptions>? phones = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddPhoneNumbers(phones).AddTelegramLinks(telegram);
        builder.Services.TryAddScoped<UserPhoneSignInService>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<ITelegramLinkCompletionHandler, PhoneSignInTelegramLinkHandler>());
        return builder;
    }
}

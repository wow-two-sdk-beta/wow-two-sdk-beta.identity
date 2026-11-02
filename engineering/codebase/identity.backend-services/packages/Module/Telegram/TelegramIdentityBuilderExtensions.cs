using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Channels;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;
using WoW.Two.Sdk.Beta.Be.Identity.Otp.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>Opts the instance's own Telegram bot into an identity registration.</summary>
public static class TelegramIdentityBuilderExtensions
{
    /// <summary>
    /// Registers the bot: <see cref="TelegramBotOptions"/> from <paramref name="configure"/>, then the host section
    /// <c>Identity:Telegram</c>, checked at boot; the bot client over <see cref="TelegramBotOptions.BotToken"/> unless one is
    /// registered already; the <c>telegram</c> OTP channel; the links over <see cref="IdentityDbContext"/>, the start links,
    /// the pending link changes (<see cref="TelegramLinkChangeService"/>) and the <see cref="TelegramUpdateHandler"/> both
    /// update modes feed; the linked chat as a verified channel
    /// (<see cref="TelegramVerifiedChannelSource"/>); the message ledger (<see cref="ITelegramMessageLedger"/>), which records
    /// every code the <c>telegram</c> channel sends and every notice of <see cref="ITelegramNoticeSender"/>, the button
    /// presses' <see cref="TelegramCallbackRouter"/>, and <see cref="TelegramMessageSweepService"/>, which cleans up due
    /// messages each minute while the bot is on; and the two update services, which each act only in their own
    /// <see cref="TelegramBotOptions.UpdateMode"/>: <see cref="TelegramPollingService"/> and, on a deployed boot,
    /// <see cref="TelegramWebhookRegistrationService"/>. Map the webhook with <c>MapTelegramWebhook</c>; register button
    /// actions with <see cref="AddTelegramCallbackHandler{THandler}"/>.
    /// </summary>
    /// <param name="builder">The identity builder.</param>
    /// <param name="configure">The switch, the bot, the webhook secret, the link and notice lifetimes and the wording.</param>
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
        services.TryAddScoped<TelegramLinkChangeService>();
        services.TryAddSingleton<TelegramBotMessages>();
        services.TryAddScoped<TelegramBotReplier>();
        services.TryAddScoped<TelegramUpdateHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAccountDeletionHandler, TelegramLinkDeletionHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IUserVerifiedChannelSource, TelegramVerifiedChannelSource>());
        services.TryAddScoped<ITelegramMessageLedger, EfTelegramMessageLedger>();
        services.TryAddScoped<ITelegramCodeMessageRecorder, TelegramCodeMessageRecorder>();
        services.TryAddScoped<ITelegramNoticeSender, TelegramNoticeSender>();
        services.TryAddScoped<TelegramCallbackRouter>();
        services.TryAddScoped<TelegramMessageSweeper>();
        services.TryAddSingleton<TelegramMessageSweepSchedule>();
        services.TryAddSingleton<TelegramPollingSchedule>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, TelegramPollingService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, TelegramWebhookRegistrationService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, TelegramMessageSweepService>());
        return builder;
    }

    /// <summary>
    /// Registers <typeparamref name="THandler"/> for presses of notice buttons whose action is <paramref name="action"/>,
    /// scoped and keyed by it (<see cref="TelegramNoticeButton.Action"/>); a later registration of the same action wins.
    /// </summary>
    /// <typeparam name="THandler">The handler.</typeparam>
    /// <param name="builder">The identity builder.</param>
    /// <param name="action">The action name, at most 64 characters, such as <c>trusted-contacts.revoke</c>.</param>
    public static IdentityBuilder AddTelegramCallbackHandler<THandler>(this IdentityBuilder builder, string action)
        where THandler : class, ITelegramCallbackHandler
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(action.Length, 64);
        builder.Services.AddKeyedScoped<ITelegramCallbackHandler, THandler>(action);
        return builder;
    }
}

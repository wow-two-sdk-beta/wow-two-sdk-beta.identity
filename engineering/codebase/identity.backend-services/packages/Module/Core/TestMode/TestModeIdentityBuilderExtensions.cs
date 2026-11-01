using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using WoW.Two.Sdk.Beta.Be.Comms.Email;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TestMode;

/// <summary>Opts test mode into an identity registration.</summary>
public static class TestModeIdentityBuilderExtensions
{
    /// <summary>The bot token test mode uses when the host sets none; it reaches no bot.</summary>
    internal const string PlaceholderBotToken = "0:test-mode";

    /// <summary>The bot user name test mode uses when the host sets none.</summary>
    internal const string PlaceholderBotUserName = "identity_test_bot";

    /// <summary>
    /// Registers test mode: <see cref="TestModeOptions"/> from <paramref name="configure"/>, then the host section
    /// <c>Identity:TestMode</c>, refused on a deployed boot; the <see cref="TestMessageSink"/>; and, only while
    /// <see cref="TestModeOptions.IsEnabled"/> is on at start, a fake Telegram channel and an email broker that keep every
    /// message in the sink, the Telegram bot switched on without a real token or webhook, and the simulator that completes a
    /// new phone sign-in link at once. Call it after <c>AddPhoneSignIn</c> and the email broker, which it wraps; map the
    /// route with <c>MapTestModeEndpoints</c>. With the switch off nothing changes.
    /// </summary>
    /// <param name="builder">The identity builder.</param>
    /// <param name="configure">The switch, from code; the host section is applied last.</param>
    public static IdentityBuilder AddTestMode(this IdentityBuilder builder, Action<TestModeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.AddModuleOptions(TestModeOptions.SectionName, configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TestModeOptions>, TestModeOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<TestMessageSink>();
        services.TryAddScoped<IPhoneSignInLinkSimulator, TestModeStartLinkSimulator>();
        services.AddOptions<TelegramBotOptions>().PostConfigure<TestModeOptions>(SwitchBotOn);
        services.SwapWhileOn<ITelegramBotClient>(provider => new TestModeTelegramBotClient(provider.GetRequiredService<TestMessageSink>(), provider.GetRequiredService<TimeProvider>()));
        services.SwapWhileOn<IEmailBroker>(provider => new TestModeEmailBroker(provider.GetRequiredService<TestMessageSink>()));
        return builder;
    }

    /// <summary>Turns the bot on with placeholders for what the host left out, in webhook mode so nothing polls or registers.</summary>
    private static void SwitchBotOn(TelegramBotOptions bot, TestModeOptions testMode)
    {
        if (!testMode.IsEnabled)
            return;

        bot.IsEnabled = true;
        bot.UpdateMode = TelegramUpdateMode.Webhook;
        if (string.IsNullOrWhiteSpace(bot.BotToken))
            bot.BotToken = PlaceholderBotToken;
        if (string.IsNullOrWhiteSpace(bot.BotUserName))
            bot.BotUserName = PlaceholderBotUserName;
        if (string.IsNullOrEmpty(bot.WebhookSecret))
            bot.WebhookSecret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
    }

    /// <summary>
    /// Re-registers <typeparamref name="TService"/> so the first resolution gives <paramref name="whileOn"/> in test mode and
    /// the earlier registration otherwise, keeping its lifetime.
    /// </summary>
    private static void SwapWhileOn<TService>(this IServiceCollection services, Func<IServiceProvider, TService> whileOn)
        where TService : class
    {
        var earlier = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(TService) && !descriptor.IsKeyedService);
        if (earlier is not null)
            services.Remove(earlier);

        services.Add(new ServiceDescriptor(
            typeof(TService),
            provider => provider.GetRequiredService<TestModeOptions>().IsEnabled ? whileOn(provider) : Resolve<TService>(provider, earlier),
            earlier?.Lifetime ?? ServiceLifetime.Singleton));
    }

    private static TService Resolve<TService>(IServiceProvider provider, ServiceDescriptor? earlier)
        where TService : class
        => earlier switch
        {
            null => throw new InvalidOperationException($"No {typeof(TService).Name} is registered; register it before AddTestMode."),
            { ImplementationInstance: TService instance } => instance,
            { ImplementationFactory: { } factory } => (TService)factory(provider),
            { ImplementationType: { } type } => (TService)ActivatorUtilities.CreateInstance(provider, type),
            _ => throw new InvalidOperationException($"The {typeof(TService).Name} registration cannot be wrapped by test mode."),
        };
}

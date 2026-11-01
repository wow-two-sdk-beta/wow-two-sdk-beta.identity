using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.OneTimeCodes;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.TestMode;
using WoW.Two.Sdk.Beta.Be.Identity.OidcServer;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace Identity.Api.Configurations;

public static partial class HostConfiguration
{
    /// <summary>
    /// Hosts sign-in by phone with a code the instance's own Telegram bot sends: one-time codes in the database, shared by
    /// every instance, the phone slice with its allowed countries, and the bot's links; and test mode, which keeps the
    /// messages in memory and fakes the bot. Everything is registered; the bot and both routes run only while
    /// <c>Identity:Telegram:IsEnabled</c> is on (or test mode is), and test mode only while <c>Identity:TestMode:IsEnabled</c> is.
    /// </summary>
    /// <remarks>
    ///   - the bot's token, user name and webhook secret come from <c>Identity:Telegram</c>; on, a missing one stops the start
    ///   - the bot's updates arrive by webhook (<c>Identity:Telegram:UpdateMode</c> <c>Webhook</c>, the default), which a deployed
    ///     boot registers with Telegram at <c>{https origin of Identity:Server:Issuer}/api/telegram/webhook</c> unless
    ///     <c>Identity:Telegram:WebhookUrl</c> names another address, or by polling (<c>Polling</c>), which only a local run allows
    ///   - the code key comes from <c>Identity:Otp:HashKey</c>, which a deployed boot requires while the bot is on
    ///   - the allowed countries come from <c>Identity:Phone:AllowedCountries</c>; empty allows every country the list knows
    ///   - test mode (<c>Identity:TestMode:IsEnabled</c>, local runs only) swaps the bot and the email broker for ones that keep
    ///     every message for <c>GET /api/test/messages?to=</c>, and completes a new phone link without a real bot
    /// </remarks>
    private static WebApplicationBuilder AddPhoneSignInByTelegram(this WebApplicationBuilder builder)
    {
        var identity = new IdentityBuilder(builder.Services)
            .AddDatabaseOtpStore()
            .AddPhoneSignIn()
            .AddTestMode();
        identity.Services.AddOptions<TelegramBotOptions>().PostConfigure<OidcServerOptions>((telegram, server) =>
        {
            // Telegram delivers to HTTPS only, so a plain-HTTP issuer (a local check) derives no address.
            if (OriginOf(server) is { } origin && origin.StartsWith(Uri.UriSchemeHttps + "://", StringComparison.OrdinalIgnoreCase))
                telegram.WebhookUrl ??= origin + TelegramWebhookEndpointRouteBuilderExtensions.DefaultPattern;
        });
        return builder;
    }

    /// <summary>Maps the phone sign-in routes under <see cref="AccountPath"/>, the bot's webhook and test mode's message route.</summary>
    private static WebApplication MapPhoneSignInByTelegram(this WebApplication app)
    {
        app.MapGroup(AccountPath).MapPhoneSignInEndpoints();
        app.MapTelegramWebhook(TelegramWebhookEndpointRouteBuilderExtensions.DefaultPattern);
        app.MapTestModeEndpoints();
        return app;
    }
}

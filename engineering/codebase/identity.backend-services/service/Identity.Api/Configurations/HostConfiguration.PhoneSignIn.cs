using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.OneTimeCodes;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace Identity.Api.Configurations;

public static partial class HostConfiguration
{
    /// <summary>
    /// Hosts sign-in by phone with a code the instance's own Telegram bot sends: one-time codes in the database, shared by
    /// every instance, the phone slice with its allowed countries, and the bot's links. Everything is registered; the
    /// bot and both routes run only while <c>Identity:Telegram:IsEnabled</c> is on.
    /// </summary>
    /// <remarks>
    ///   - the bot's token, user name and webhook secret come from <c>Identity:Telegram</c>; on, a missing one stops the start
    ///   - the code key comes from <c>Identity:Otp:HashKey</c>, which a deployed boot requires while the bot is on
    ///   - the allowed countries come from <c>Identity:Phone:AllowedCountries</c>; empty allows every country the list knows
    /// </remarks>
    private static WebApplicationBuilder AddPhoneSignInByTelegram(this WebApplicationBuilder builder)
    {
        new IdentityBuilder(builder.Services)
            .AddDatabaseOtpStore()
            .AddPhoneSignIn();
        return builder;
    }

    /// <summary>Maps the phone sign-in routes under <see cref="AccountPath"/> and the bot's webhook.</summary>
    private static WebApplication MapPhoneSignInByTelegram(this WebApplication app)
    {
        app.MapGroup(AccountPath).MapPhoneSignInEndpoints();
        app.MapTelegramWebhook(TelegramWebhookEndpointRouteBuilderExtensions.DefaultPattern);
        return app;
    }
}

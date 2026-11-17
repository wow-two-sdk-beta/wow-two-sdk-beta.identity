using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>Opts app notices into an identity registration.</summary>
public static class AppNoticeIdentityBuilderExtensions
{
    /// <summary>
    /// Registers app notices over the instance's Telegram bot (<see cref="TelegramIdentityBuilderExtensions.AddTelegramLinks"/>,
    /// added here when missing): <see cref="AppNoticeService"/>, the in-process <see cref="IAppNoticeBroker"/>
    /// (<see cref="TelegramAppNoticeBroker"/>, sending as <see cref="AppNoticeOptions.LocalClientId"/>) and the
    /// <see cref="AppNoticePace"/>. Settings come from <paramref name="configure"/>, then the host section
    /// <c>Identity:Notices</c>. The keys and mutes store, the mute button, <c>/stop</c> and the account deletion come with the
    /// bot. The service maps the HTTP route itself.
    /// </summary>
    /// <param name="builder">The identity builder.</param>
    /// <param name="configure">The limits and the host's app name, from code; the host section is applied last.</param>
    public static IdentityBuilder AddAppNotices(this IdentityBuilder builder, Action<AppNoticeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddTelegramLinks();
        var services = builder.Services;
        services.AddModuleOptions(
            AppNoticeOptions.SectionName,
            configure,
            options => options
                .ValidateSetting(nameof(AppNoticeOptions.LocalClientId), "required",
                    o => !string.IsNullOrWhiteSpace(o.LocalClientId) && o.LocalClientId.Length <= AppNoticeOptions.MaxClientIdLength,
                    $"must be 1–{AppNoticeOptions.MaxClientIdLength} characters.")
                .ValidateSetting(nameof(AppNoticeOptions.PermitLimit), "range", o => o.PermitLimit > 0, "must be positive.")
                .ValidateSetting(nameof(AppNoticeOptions.Window), "range", o => o.Window > TimeSpan.Zero, "must be positive.")
                .ValidateSetting(nameof(AppNoticeOptions.IdempotencyWindow), "range",
                    o => o.IdempotencyWindow >= TimeSpan.FromHours(1), "must be an hour or more.")
                .ValidateSetting(nameof(AppNoticeOptions.MaxPaceWait), "range",
                    o => o.MaxPaceWait >= TimeSpan.Zero && o.MaxPaceWait <= TimeSpan.FromMinutes(1), "must be 0 to 1 minute."));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<AppNoticePace>();
        services.TryAddScoped<AppNoticeService>();
        services.TryAddScoped<IAppNoticeBroker, TelegramAppNoticeBroker>();
        return builder;
    }
}

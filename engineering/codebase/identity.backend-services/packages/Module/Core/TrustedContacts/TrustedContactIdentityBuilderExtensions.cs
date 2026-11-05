using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>Opts trusted contacts, and the own phone beside them, into an identity registration.</summary>
public static class TrustedContactIdentityBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="TrustedContactService"/>, <see cref="OwnPhoneService"/> and their store over
    /// <c>identity_trusted_contacts</c>; the bot's <c>add-phone</c> link handler and the notice's
    /// <see cref="TrustedContactService.RevokeAction"/> button handler; <see cref="DeviceConfirmationService"/>, its store and its
    /// two buttons' handler; and the account deletion handler. Settings come from
    /// <paramref name="configure"/>, then the host section <c>Identity:TrustedContacts</c>, checked at boot and in
    /// <c>config check</c>. It needs phone sign-in (the bot), and reads invitations, organizations, sessions, the sign-in log
    /// and pictures where registered; map its routes with <c>MapTrustedContactEndpoints</c>.
    /// </summary>
    /// <param name="builder">The identity builder.</param>
    /// <param name="configure">The switch and the cap, from code; the host section is applied last.</param>
    public static IdentityBuilder AddTrustedContacts(this IdentityBuilder builder, Action<TrustedContactOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.AddModuleOptions(
            TrustedContactOptions.SectionName,
            configure,
            options => options.ValidateSetting(
                nameof(TrustedContactOptions.MaxPerPerson),
                "range",
                o => o.MaxPerPerson is >= 1 and <= TrustedContactOptions.MaxPerPersonLimit,
                $"must be 1–{TrustedContactOptions.MaxPerPersonLimit}."));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TrustedContactOptions>, TrustedContactOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ITrustedContactRepository, EfTrustedContactRepository>();
        services.TryAddScoped<TrustedContactService>();
        services.TryAddScoped<OwnPhoneService>();
        services.TryAddScoped<IDeviceConfirmationRepository, EfDeviceConfirmationRepository>();
        services.TryAddScoped<DeviceConfirmationService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITelegramLinkCompletionHandler, AddPhoneTelegramLinkHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAccountDeletionHandler, TrustedContactDeletionHandler>());
        builder.AddTelegramCallbackHandler<TrustedContactRevokeHandler>(TrustedContactService.RevokeAction);
        builder.AddTelegramCallbackHandler<DeviceConfirmationDecisionHandler>(DeviceConfirmationService.ApproveAction);
        builder.AddTelegramCallbackHandler<DeviceConfirmationDecisionHandler>(DeviceConfirmationService.DenyAction);
        return builder;
    }
}

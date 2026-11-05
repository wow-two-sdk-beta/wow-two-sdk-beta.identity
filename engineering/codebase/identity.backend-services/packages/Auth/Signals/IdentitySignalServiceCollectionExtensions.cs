using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;
using WoW.Two.Sdk.Beta.Be.Messaging;
using WoW.Two.Sdk.Beta.Be.Messaging.Webhooks;
using WoW.Two.Sdk.Beta.Be.Messaging.Webhooks.Inbound;
using WoW.Two.Sdk.Beta.Be.Messaging.Webhooks.Inbound.Validators;

namespace WoW.Two.Sdk.Beta.Be.Identity.Signals;

/// <summary>Registers the receiving side of Identity's revocation signals.</summary>
public static class IdentitySignalServiceCollectionExtensions
{
    /// <summary>
    /// Registers what <c>MapIdentitySignals</c> needs: the options, the signature validator under
    /// <see cref="WebhookSchemeNameConstants.KeySigned"/>, the <see cref="IdentitySignalReceiverConstants.ReceiverName"/> receiver
    /// and an in-memory event bus carrying this package's handlers. Safe to call more than once, and beside a host's own bus.
    /// </summary>
    /// <remarks>
    /// A broker registers before this call: its registration, like the in-memory one, adds only what is missing, so the first
    /// transport wins. Handlers stay as they are either way.
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">The signal URL and the trusted issuers; every call's delegate applies.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddIdentitySignalReceiver(this IServiceCollection services, Action<IdentitySignalReceiverOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.AddOptions<IdentitySignalReceiverOptions>();
        if (configure is not null)
            options.Configure(configure);
        options.ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<IdentitySignalReceiverOptions>, IdentitySignalReceiverOptionsValidator>());

        services.AddHttpContextAccessor();
        services.AddInboundWebhooks(inbound => inbound.Receivers[IdentitySignalReceiverConstants.ReceiverName] = new WebhookReceiverOptions { Scheme = WebhookSchemeNameConstants.KeySigned });
        services.TryAddKeyedSingleton<IWebhookSignatureValidator, IdentitySignalSignatureValidator>(WebhookSchemeNameConstants.KeySigned);

        // The Abstractions scan only names the contracts; the handlers come in once, since a repeated scan would run each twice.
        services.AddSignalHandlers();
        services.AddInMemoryEventBus(typeof(SessionEndedEvent).Assembly);
        return services;
    }

    /// <summary>
    /// Registers this package's <see cref="IEventHandler{TEvent}"/>s once, whichever registration asks first. Each takes what it
    /// feeds as optional, so a host without a deny list or a job tracker still resolves them.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    internal static IServiceCollection AddSignalHandlers(this IServiceCollection services)
    {
        var isRegistered = services.Any(descriptor =>
            descriptor.ServiceType == typeof(IEventHandler<SessionEndedEvent>) && descriptor.ImplementationType == typeof(TokenDenyListSignalHandler));
        return isRegistered ? services : services.AddEventHandlersFromAssemblies(typeof(TokenDenyListSignalHandler).Assembly, typeof(SessionEndedEvent).Assembly);
    }
}

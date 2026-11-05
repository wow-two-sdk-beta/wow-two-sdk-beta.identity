using WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;
using WoW.Two.Sdk.Beta.Be.Messaging;
using WoW.Two.Sdk.Beta.Be.Messaging.Webhooks;

namespace WoW.Two.Sdk.Beta.Be.Identity.Signals;

/// <summary>Carries each event from the local bus to every service that named a signal URL; a broker can replace this with no receiver's handler touched.</summary>
/// <param name="webhooks">Signs and delivers, with retries.</param>
public sealed class OidcSignalForwarder(IWebhookPublisher webhooks) : IEventHandler<SessionEndedEvent>, IEventHandler<AccessChangedEvent>
{
    /// <inheritdoc />
    public ValueTask HandleAsync(EventContext<SessionEndedEvent> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return webhooks.PublishAsync(context.Event, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask HandleAsync(EventContext<AccessChangedEvent> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return webhooks.PublishAsync(context.Event, cancellationToken);
    }
}

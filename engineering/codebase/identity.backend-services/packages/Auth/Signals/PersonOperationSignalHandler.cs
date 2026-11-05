using WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;
using WoW.Two.Sdk.Beta.Be.Jobs.Core.PersonOperations;
using WoW.Two.Sdk.Beta.Be.Messaging;

namespace WoW.Two.Sdk.Beta.Be.Identity.Signals;

/// <summary>
/// Bridges signals to the person operations running here. The kinds carry the meaning: a role change continues, so
/// <see cref="AccessChangeKind.Changed"/> reaches no operation; access loss cancels. A signal is at-least-once, and a repeat cancels nothing new.
/// </summary>
/// <param name="tracker">The tracker; null on a host without <c>AddPersonOperations</c>.</param>
internal sealed class PersonOperationSignalHandler(IPersonOperationTracker? tracker = null) : IEventHandler<SessionEndedEvent>, IEventHandler<AccessChangedEvent>
{
    /// <inheritdoc />
    public ValueTask HandleAsync(EventContext<SessionEndedEvent> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        tracker?.SessionEnded(context.Event.SessionId, context.Event.Subject);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask HandleAsync(EventContext<AccessChangedEvent> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Event.Kind == AccessChangeKind.Removed)
            tracker?.AccessLost(context.Event.Subject, context.Event.OrganizationId);

        return ValueTask.CompletedTask;
    }
}

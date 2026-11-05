using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;
using WoW.Two.Sdk.Beta.Be.Messaging;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

/// <summary>
/// Reacts to the signals of this BFF's own instance: an ended session ends here, and a changed access makes the person's next
/// request refresh first. A signal from another issuer the host trusts as a resource reaches no session here, since a <c>sid</c>
/// and a <c>sub</c> mean something only at the issuer that wrote them. Both reactions are idempotent.
/// </summary>
/// <param name="sessions">The session service.</param>
/// <param name="accessChanges">The people whose access changed.</param>
/// <param name="options">The instance this BFF signs in through.</param>
/// <param name="time">This host's clock: the session compares it with its own, so the instance's clock skew can't hide a change.</param>
internal sealed class BffSignalHandler(BffSessionService sessions, BffAccessChangeRepository accessChanges, IOptions<IdentityBffOptions> options, TimeProvider time)
    : IEventHandler<SessionEndedEvent>, IEventHandler<AccessChangedEvent>
{
    /// <inheritdoc />
    public async ValueTask HandleAsync(EventContext<SessionEndedEvent> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (IsOwnInstance(context.Event.Issuer))
            await sessions.EndAsync(context.Event.SessionId, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask HandleAsync(EventContext<AccessChangedEvent> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (IsOwnInstance(context.Event.Issuer))
            await accessChanges.RecordAsync(context.Event.Subject, time.GetUtcNow(), cancellationToken);
    }

    private bool IsOwnInstance(string? issuer)
        => issuer is not null && options.Value.AuthorityIssuers.Contains(issuer, StringComparer.Ordinal);
}

using WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;
using WoW.Two.Sdk.Beta.Be.Messaging;

namespace WoW.Two.Sdk.Beta.Be.Identity.Signals;

/// <summary>
/// Adds what a signal says to the deny list. Both kinds of access change deny tokens from before the change, since either way that
/// token holds the old answer. A signal naming no issuer can't match a token, so it adds nothing; entries are idempotent.
/// </summary>
/// <param name="denyList">The list; null on a host without <c>AddTokenDenyList</c>.</param>
internal sealed class TokenDenyListSignalHandler(ITokenDenyList? denyList = null) : IEventHandler<SessionEndedEvent>, IEventHandler<AccessChangedEvent>
{
    /// <inheritdoc />
    public async ValueTask HandleAsync(EventContext<SessionEndedEvent> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var signal = context.Event;
        if (denyList is not null && !string.IsNullOrEmpty(signal.Issuer))
            await denyList.AddSessionAsync(signal.Issuer, signal.SessionId, signal.TokensExpireBy, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask HandleAsync(EventContext<AccessChangedEvent> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var signal = context.Event;
        if (denyList is not null && !string.IsNullOrEmpty(signal.Issuer))
            await denyList.AddAccessAsync(signal.Issuer, signal.Subject, signal.OrganizationId, signal.At, signal.TokensExpireBy, cancellationToken);
    }
}

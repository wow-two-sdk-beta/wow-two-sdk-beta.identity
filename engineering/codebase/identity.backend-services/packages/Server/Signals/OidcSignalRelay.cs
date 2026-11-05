using System.Globalization;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.AccessChanges;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;
using WoW.Two.Sdk.Beta.Be.Identity.OidcServer;
using WoW.Two.Sdk.Beta.Be.Messaging.Buses;

namespace WoW.Two.Sdk.Beta.Be.Identity.Signals;

/// <summary>
/// Turns an ended session and a changed access into events on the local bus, off the request, so a sign-out never waits on a
/// slow service. Each event names the issuer and when the last token from before has expired.
/// </summary>
/// <param name="bus">The local event bus.</param>
/// <param name="options">The issuer and the token lifetimes.</param>
/// <param name="time">The clock.</param>
internal sealed class OidcSignalRelay(IEventBus bus, OidcServerOptions options, TimeProvider time) : ISessionRevocationHandler, IAccessChangeHandler
{
    /// <summary>Past a token's own <c>exp</c>, what a verifier's clock skew may still accept.</summary>
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public async Task HandleAsync(IReadOnlyList<IdentitySession> sessions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        if (!HasTargets())
            return;

        foreach (var session in sessions)
        {
            await bus.PublishAsync(
                new SessionEndedEvent
                {
                    Issuer = options.Issuer?.AbsoluteUri,
                    SessionId = session.Id.ToString("D", CultureInfo.InvariantCulture),
                    Subject = session.UserId.ToString("D", CultureInfo.InvariantCulture),
                    At = time.GetUtcNow(),
                    TokensExpireBy = TokensExpireBy(),
                },
                cancellationToken: cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task HandleAsync(AccessChangedEvent change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (HasTargets())
            await bus.PublishAsync(change with { Issuer = options.Issuer?.AbsoluteUri, TokensExpireBy = TokensExpireBy() }, cancellationToken: cancellationToken);
    }

    private bool HasTargets()
        => options.Clients.Any(client => client.SignalUrl is not null) || options.Resources.Any(resource => resource.SignalUrl is not null);

    private DateTimeOffset TokensExpireBy()
        => time.GetUtcNow() + (options.AccessTokenLifetime > options.ServiceTokenLifetime ? options.AccessTokenLifetime : options.ServiceTokenLifetime) + ExpiryMargin;
}

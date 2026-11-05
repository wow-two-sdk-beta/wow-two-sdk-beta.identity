using WoW.Two.Sdk.Beta.Be.Identity.OidcServer;
using WoW.Two.Sdk.Beta.Be.Messaging.Webhooks;

namespace WoW.Two.Sdk.Beta.Be.Identity.Signals;

/// <summary>Reads who hears signals from the registry: every client and resource that names a signal URL, once per URL.</summary>
/// <param name="options">The registry.</param>
internal sealed class OidcSignalSubscriptionRepository(OidcServerOptions options) : IWebhookSubscriptionRepository
{
    private IReadOnlyList<WebhookSubscription>? _subscriptions;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<WebhookSubscription>> GetMatchingAsync(string eventType, CancellationToken cancellationToken)
        => ValueTask.FromResult<IReadOnlyList<WebhookSubscription>>([.. Subscriptions.Where(subscription => subscription.Matches(eventType))]);

    /// <inheritdoc />
    public ValueTask AddAsync(WebhookSubscription subscription, CancellationToken cancellationToken)
        => throw new NotSupportedException("Signal targets come from Identity:Server; name a SignalUrl on the client or resource.");

    /// <inheritdoc />
    public ValueTask<WebhookSubscription?> FindAsync(string id, CancellationToken cancellationToken)
        => ValueTask.FromResult(Subscriptions.FirstOrDefault(subscription => subscription.Id == id));

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<WebhookSubscription>> ListAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult(Subscriptions);

    /// <inheritdoc />
    public ValueTask<bool> RemoveAsync(string id, CancellationToken cancellationToken)
        => throw new NotSupportedException("Signal targets come from Identity:Server; remove the SignalUrl.");

    private IReadOnlyList<WebhookSubscription> Subscriptions => _subscriptions ??=
    [
        .. options.Clients.Select(client => client.SignalUrl)
            .Concat(options.Resources.Select(resource => resource.SignalUrl))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Select(url => new WebhookSubscription
            {
                Id = url,
                Url = new Uri(url, UriKind.Absolute),
                SignatureScheme = WebhookSchemeNameConstants.KeySigned,
                Description = "Identity revocation signals",
            }),
    ];
}

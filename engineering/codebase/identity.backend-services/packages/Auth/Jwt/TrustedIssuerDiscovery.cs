using System.Collections.Concurrent;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace WoW.Two.Sdk.Beta.Be.Identity.Jwt;

/// <summary>
/// Caches one configuration manager per metadata address, with the manager's defaults: a 12-hour automatic refresh and
/// a 5-minute minimum between asked refreshes.
/// </summary>
internal sealed class TrustedIssuerDiscovery : IDisposable
{
    private static readonly TimeSpan BackchannelTimeout = TimeSpan.FromMinutes(1);
    private const long MaximumDocumentBytes = 10 * 1024 * 1024;

    private readonly ConcurrentDictionary<string, IssuerDocument> _documents = new(StringComparer.Ordinal);
    private readonly HttpClient _client;
    private readonly HttpDocumentRetriever _retriever;

    /// <param name="backchannel">The handler requests go through; null uses the network. The caller keeps owning it.</param>
    /// <param name="requireHttps">Whether a metadata or key address must use HTTPS.</param>
    public TrustedIssuerDiscovery(HttpMessageHandler? backchannel, bool requireHttps)
    {
        _client = backchannel is null ? new HttpClient() : new HttpClient(backchannel, disposeHandler: false);
        _client.Timeout = BackchannelTimeout;
        _client.MaxResponseContentBufferSize = MaximumDocumentBytes;
        _retriever = new HttpDocumentRetriever(_client) { RequireHttps = requireHttps };
    }

    /// <summary>Reads the issuer's current signing keys, fetching its documents the first time.</summary>
    /// <param name="metadataAddress">The issuer's discovery address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ICollection<SecurityKey>> GetKeysAsync(Uri metadataAddress, CancellationToken cancellationToken) =>
        DocumentOf(metadataAddress).GetKeysAsync(cancellationToken);

    /// <summary>Asks for the issuer's documents again and reads its signing keys once they arrive.</summary>
    /// <remarks>
    /// Within the minimum between refreshes, or when the fetch fails or runs long, this answers the keys it already holds.
    /// </remarks>
    /// <param name="metadataAddress">The issuer's discovery address.</param>
    /// <param name="cancellationToken">Stops this caller's wait; the refresh goes on for the others.</param>
    public Task<ICollection<SecurityKey>> RefreshKeysAsync(Uri metadataAddress, CancellationToken cancellationToken) =>
        DocumentOf(metadataAddress).RefreshKeysAsync().WaitAsync(cancellationToken);

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();

    private IssuerDocument DocumentOf(Uri metadataAddress) =>
        _documents.GetOrAdd(metadataAddress.AbsoluteUri, address => new IssuerDocument(
            new ConfigurationManager<OpenIdConnectConfiguration>(address, new OpenIdConnectConfigurationRetriever(), _retriever)));

    /// <summary>
    /// One issuer's manager, plus a wait the manager lacks: a caller retrying a token needs the new keys, not a promise of
    /// them.
    /// </summary>
    private sealed class IssuerDocument(ConfigurationManager<OpenIdConnectConfiguration> manager)
    {
        private static readonly TimeSpan RefreshWait = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan RefreshPoll = TimeSpan.FromMilliseconds(20);

        // Past the manager's own minimum: a refresh it would ignore would hold requests for the whole wait.
        private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(1);

        private readonly Lock _gate = new();
        private Task<ICollection<SecurityKey>>? _refresh;
        private DateTimeOffset _nextRefresh = DateTimeOffset.MinValue;

        public async Task<ICollection<SecurityKey>> GetKeysAsync(CancellationToken cancellationToken) =>
            (await manager.GetConfigurationAsync(cancellationToken)).SigningKeys;

        /// <summary>Joins the refresh under way, starts one when the minimum has passed, or answers the current keys.</summary>
        public Task<ICollection<SecurityKey>> RefreshKeysAsync()
        {
            lock (_gate)
            {
                if (_refresh is { IsCompleted: false })
                    return _refresh;

                var now = TimeProvider.System.GetUtcNow();
                if (now < _nextRefresh)
                    return GetKeysAsync(CancellationToken.None);

                _nextRefresh = now + manager.RefreshInterval + RefreshMargin;
                _refresh = Task.Run(RefreshAndWaitAsync);
                return _refresh;
            }
        }

        /// <remarks>
        /// <c>RequestRefresh</c> returns before the new document exists on the IdentityModel versions consumers resolve (8.14
        /// and later fetch on a background task and keep answering the old document; 8.0 fetches on the next read), and
        /// signals nothing, so this reads until the manager holds another document. A failed fetch keeps the old one.
        /// </remarks>
        private async Task<ICollection<SecurityKey>> RefreshAndWaitAsync()
        {
            var before = await manager.GetConfigurationAsync(CancellationToken.None);
            manager.RequestRefresh();

            using var timeout = new CancellationTokenSource(RefreshWait);
            try
            {
                while (true)
                {
                    var current = await manager.GetConfigurationAsync(timeout.Token);
                    if (!ReferenceEquals(current, before))
                        return current.SigningKeys;

                    await Task.Delay(RefreshPoll, timeout.Token);
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                return before.SigningKeys;
            }
        }
    }
}

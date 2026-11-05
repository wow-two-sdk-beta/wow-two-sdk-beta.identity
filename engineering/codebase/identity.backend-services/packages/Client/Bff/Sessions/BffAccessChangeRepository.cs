using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

/// <summary>
/// Keeps, per person, when this host last heard their access changed, so their next request refreshes first; the session store has no index by
/// person. An entry lasts <see cref="IdentityBffOptions.SessionLifetime"/>: an access token runs out long before that, and its own
/// refresh then brings the same fresh claims.
/// </summary>
/// <param name="cache">The distributed cache.</param>
/// <param name="options">The session lifetime.</param>
internal sealed class BffAccessChangeRepository(IDistributedCache cache, IOptions<IdentityBffOptions> options)
{
    private const string KeyPrefix = "identity-bff:access-change:";

    /// <summary>Records a change; a repeated or late signal never moves the time back.</summary>
    /// <param name="subject">The person's <c>sub</c>.</param>
    /// <param name="at">When this host heard of it, on this host's clock.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RecordAsync(string subject, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);
        var latest = await FindAsync(subject, cancellationToken) is { } known && known > at ? known : at;
        await cache.SetAsync(
            KeyPrefix + subject,
            JsonSerializer.SerializeToUtf8Bytes(latest),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = options.Value.SessionLifetime },
            cancellationToken);
    }

    /// <summary>When the person's access last changed; null when nothing is remembered.</summary>
    /// <param name="subject">The person's <c>sub</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<DateTimeOffset?> FindAsync(string subject, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);
        var bytes = await cache.GetAsync(KeyPrefix + subject, cancellationToken);
        if (bytes is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<DateTimeOffset>(bytes);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

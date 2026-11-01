using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

/// <summary>
/// Provides the default <see cref="IBffSessionRepository"/> over <see cref="IDistributedCache"/>: one entry per session,
/// evicted at the session's absolute end. The in-memory cache serves one instance; register a shared cache (Redis, SQL
/// Server) before several instances serve the BFF.
/// </summary>
/// <param name="cache">The distributed cache.</param>
public sealed class DistributedCacheBffSessionRepository(IDistributedCache cache) : IBffSessionRepository
{
    /// <summary>Holds the prefix of every entry's key.</summary>
    private const string KeyPrefix = "identity-bff:session:";

    /// <inheritdoc />
    public async Task<BffSessionRecord?> FindAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        var bytes = await cache.GetAsync(KeyPrefix + sessionId, cancellationToken);
        if (bytes is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<BffSessionRecord>(bytes);
        }
        catch (JsonException)
        {
            // An entry this repository did not write reads as no session.
            return null;
        }
    }

    /// <inheritdoc />
    public Task SaveAsync(BffSessionRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return cache.SetAsync(
            KeyPrefix + record.SessionId,
            JsonSerializer.SerializeToUtf8Bytes(record),
            new DistributedCacheEntryOptions { AbsoluteExpiration = record.ExpiresAt },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        return cache.RemoveAsync(KeyPrefix + sessionId, cancellationToken);
    }
}

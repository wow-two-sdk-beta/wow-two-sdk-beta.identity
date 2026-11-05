using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace WoW.Two.Sdk.Beta.Be.Identity.Signals;

/// <summary>
/// Provides the default <see cref="ITokenDenyList"/> over <see cref="IDistributedCache"/>: one entry per session and one per
/// person and organization, evicted once the tokens it refuses have expired. Each entry also states its own end, so the list
/// goes by this host's clock, whatever the cache's precision. Two replicas writing one entry at once can lose the later
/// <c>notBefore</c>; the token's lifetime stays the limit.
/// </summary>
/// <param name="cache">The distributed cache.</param>
/// <param name="options">The default lifetime.</param>
/// <param name="time">The clock.</param>
internal sealed class DistributedCacheTokenDenyList(IDistributedCache cache, IOptions<DenyListOptions> options, TimeProvider time) : ITokenDenyList
{
    private const string SessionPrefix = "identity:deny:session:";
    private const string AccessPrefix = "identity:deny:access:";

    /// <inheritdoc />
    public async Task AddSessionAsync(string issuer, string sessionId, DateTimeOffset? until, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(issuer);
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        var key = SessionPrefix + Hash(issuer, sessionId);
        var end = await LaterEndAsync(key, until, cancellationToken);
        if (end > time.GetUtcNow())
            await WriteAsync(key, new DenyEntry { Until = end }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddAccessAsync(string issuer, string subject, string organizationId, DateTimeOffset notBefore, DateTimeOffset? until, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(issuer);
        ArgumentException.ThrowIfNullOrEmpty(subject);
        ArgumentException.ThrowIfNullOrEmpty(organizationId);

        var key = AccessPrefix + Hash(issuer, subject, organizationId);
        var existing = await ReadAsync(key, cancellationToken);
        var end = Later(existing?.Until, until ?? DefaultEnd());
        if (end <= time.GetUtcNow())
            return;

        var latest = Later(existing?.NotBefore, notBefore);
        await WriteAsync(key, new DenyEntry { Until = end, NotBefore = latest }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsDeniedAsync(string issuer, string? sessionId, string? subject, string? organizationId, DateTimeOffset? issuedAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(issuer);

        var now = time.GetUtcNow();
        if (!string.IsNullOrEmpty(sessionId)
            && await ReadAsync(SessionPrefix + Hash(issuer, sessionId), cancellationToken) is { } session
            && session.Until > now)
            return true;

        if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(organizationId))
            return false;

        return await ReadAsync(AccessPrefix + Hash(issuer, subject, organizationId), cancellationToken) is { } access
            && access.Until > now
            && (issuedAt is not { } issued || issued < access.NotBefore);
    }

    private DateTimeOffset DefaultEnd() => time.GetUtcNow() + options.Value.DefaultEntryLifetime;

    private async Task<DateTimeOffset> LaterEndAsync(string key, DateTimeOffset? until, CancellationToken cancellationToken)
        => Later((await ReadAsync(key, cancellationToken))?.Until, until ?? DefaultEnd());

    private static DateTimeOffset Later(DateTimeOffset? first, DateTimeOffset second)
        => first is { } value && value > second ? value : second;

    private Task WriteAsync(string key, DenyEntry entry, CancellationToken cancellationToken)
        => cache.SetAsync(
            key,
            JsonSerializer.SerializeToUtf8Bytes(entry),
            new DistributedCacheEntryOptions { AbsoluteExpiration = entry.Until },
            cancellationToken);

    private async Task<DenyEntry?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        var bytes = await cache.GetAsync(key, cancellationToken);
        if (bytes is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<DenyEntry>(bytes);
        }
        catch (JsonException)
        {
            // An entry this list did not write reads as no entry.
            return null;
        }
    }

    /// <summary>A fixed-length key over length-prefixed parts, so two sets of ids can't collide and a long id can't overrun a store's key limit.</summary>
    private static string Hash(params string[] parts)
        => Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(parts.Select(part => $"{part.Length}:{part}")))));

    /// <summary>One stored entry.</summary>
    private sealed record DenyEntry
    {
        /// <summary>Gets when the entry goes.</summary>
        public required DateTimeOffset Until { get; init; }

        /// <summary>Gets the change a token must be issued after; unused on a session's entry, which refuses whatever the <c>iat</c>.</summary>
        public DateTimeOffset NotBefore { get; init; }
    }
}

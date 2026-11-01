using System.Collections.Concurrent;

namespace WoW.Two.Sdk.Beta.Be.Identity.Otp;

/// <summary>Accesses one-time-code records in process memory; for tests and single-process hosts.</summary>
public sealed class MemoryOtpRepository : IOtpRepository
{
    private static readonly TimeSpan PruneGrace = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<Guid, OtpRecord> _records = new();
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the store over a time source.</summary>
    /// <param name="timeProvider">Time source for rate-limit and prune checks.</param>
    public MemoryOtpRepository(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<bool> HasRecentAsync(string subjectHash, string scope, string? originHash, TimeSpan window, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        var cutoff = _timeProvider.GetUtcNow() - window;
        var hasRecent = _records.Values.Any(r => r.CreatedAt >= cutoff
            && string.Equals(r.Scope, scope, StringComparison.Ordinal)
            && ((!r.IsConsumed && string.Equals(r.SubjectHash, subjectHash, StringComparison.Ordinal))
                || (originHash is not null && string.Equals(r.OriginHash, originHash, StringComparison.Ordinal))));
        return Task.FromResult(hasRecent);
    }

    /// <inheritdoc />
    public Task SaveAsync(OtpRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        Prune();
        _records[record.Id] = record;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<OtpRecord?> FindLatestPendingAsync(string subjectHash, string scope, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        var latest = _records.Values
            .Where(r => !r.IsConsumed
                && string.Equals(r.SubjectHash, subjectHash, StringComparison.Ordinal)
                && string.Equals(r.Scope, scope, StringComparison.Ordinal))
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefault();
        return Task.FromResult(latest);
    }

    /// <inheritdoc />
    public Task<bool> TryCountAttemptAsync(Guid recordId, int maxAttempts, CancellationToken cancellationToken = default)
        => Task.FromResult(TryMutate(recordId, r => !r.IsConsumed && r.Attempts < maxAttempts, r => r with { Attempts = r.Attempts + 1 }));

    /// <inheritdoc />
    public Task<bool> TryConsumeAsync(Guid recordId, CancellationToken cancellationToken = default)
        => Task.FromResult(TryMutate(recordId, r => !r.IsConsumed, r => r with { IsConsumed = true }));

    /// <summary>Applies <paramref name="update"/> while <paramref name="condition"/> holds, retrying a lost race.</summary>
    private bool TryMutate(Guid recordId, Func<OtpRecord, bool> condition, Func<OtpRecord, OtpRecord> update)
    {
        while (_records.TryGetValue(recordId, out var current))
        {
            if (!condition(current))
                return false;
            if (_records.TryUpdate(recordId, update(current), current))
                return true;
        }

        return false;
    }

    private void Prune()
    {
        var horizon = _timeProvider.GetUtcNow() - PruneGrace;
        foreach (var entry in _records)
        {
            if (entry.Value.ExpiresAt < horizon)
            {
                _records.TryRemove(entry.Key, out _);
            }
        }
    }
}

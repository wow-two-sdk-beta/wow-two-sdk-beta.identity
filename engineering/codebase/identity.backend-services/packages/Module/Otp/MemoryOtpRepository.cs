using System.Collections.Concurrent;

namespace WoW.Two.Sdk.Beta.Be.Identity.Otp;

/// <summary>Accesses one-time-code records in process memory; for tests and single-process hosts.</summary>
public sealed class MemoryOtpRepository : IOtpRepository
{
    private static readonly TimeSpan PruneGrace = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<Guid, OtpRecord> _records = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _retention;

    /// <summary>Creates the store over a time source.</summary>
    /// <param name="timeProvider">Time source for rate-limit and prune checks.</param>
    /// <param name="options">The OTP options whose failure window pruning keeps; null keeps an hour past expiry.</param>
    public MemoryOtpRepository(TimeProvider timeProvider, OtpOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
        _retention = options?.SubjectFailureWindow is { } window && window > PruneGrace ? window : PruneGrace;
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

        var latest = Of(subjectHash, scope)
            .Where(r => !r.IsConsumed)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.ExpiresAt)
            .ThenByDescending(r => r.Id)
            .FirstOrDefault();
        return Task.FromResult(latest);
    }

    /// <inheritdoc />
    public Task RetirePendingAsync(string subjectHash, string scope, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        foreach (var record in Of(subjectHash, scope).Where(r => !r.IsConsumed && r.ExpiresAt > at).ToList())
            TryMutate(record.Id, r => !r.IsConsumed && r.ExpiresAt > at, r => r with { ExpiresAt = at });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> CountFailedAttemptsAsync(string subjectHash, string scope, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        // A spent code counted its successful attempt too.
        return Task.FromResult(Of(subjectHash, scope).Where(r => r.CreatedAt >= since).Sum(r => r.IsConsumed ? r.Attempts - 1 : r.Attempts));
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid recordId, CancellationToken cancellationToken = default)
    {
        _records.TryRemove(recordId, out _);
        return Task.CompletedTask;
    }

    private IEnumerable<OtpRecord> Of(string subjectHash, string scope)
        => _records.Values.Where(r => string.Equals(r.SubjectHash, subjectHash, StringComparison.Ordinal) && string.Equals(r.Scope, scope, StringComparison.Ordinal));

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
        var horizon = _timeProvider.GetUtcNow() - _retention;
        foreach (var entry in _records)
        {
            if (entry.Value.ExpiresAt < horizon)
            {
                _records.TryRemove(entry.Key, out _);
            }
        }
    }
}

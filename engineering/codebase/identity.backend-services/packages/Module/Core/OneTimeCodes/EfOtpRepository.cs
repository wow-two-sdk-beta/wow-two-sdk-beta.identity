using Microsoft.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.OneTimeCodes;

/// <summary>
/// Accesses one-time codes in <c>identity_otp_codes</c> through <see cref="IdentityDbContext"/>, so every instance on the
/// schema shares them. Counting an attempt and consuming are single conditional updates; saving prunes rows an hour past
/// their expiry, or past <see cref="OtpOptions.SubjectFailureWindow"/> when that is longer, so the failure budget still
/// counts them.
/// </summary>
/// <param name="context">The identity context.</param>
/// <param name="time">The clock pruning measures against.</param>
/// <param name="options">The OTP options whose failure window pruning keeps; null keeps the hour.</param>
public sealed class EfOtpRepository(IdentityDbContext context, TimeProvider time, OtpOptions? options = null) : IOtpRepository
{
    private static readonly TimeSpan PruneGrace = TimeSpan.FromHours(1);

    private DbSet<IdentityOtpCode> Codes => context.Set<IdentityOtpCode>();

    private TimeSpan Retention => options?.SubjectFailureWindow is { } window && window > PruneGrace ? window : PruneGrace;

    /// <inheritdoc />
    public Task<bool> HasRecentAsync(string subjectHash, string scope, string? originHash, TimeSpan window, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        var cutoff = time.GetUtcNow() - window;
        var recent = Codes.AsNoTracking().Where(c => c.Scope == scope && c.CreatedAt >= cutoff);
        return originHash is null
            ? recent.AnyAsync(c => c.SubjectHash == subjectHash && c.ConsumedAt == null, cancellationToken)
            : recent.AnyAsync(c => (c.SubjectHash == subjectHash && c.ConsumedAt == null) || c.OriginHash == originHash, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveAsync(OtpRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var horizon = time.GetUtcNow() - Retention;
        await Codes.Where(c => c.ExpiresAt < horizon).ExecuteDeleteAsync(cancellationToken);

        var row = new IdentityOtpCode
        {
            Id = record.Id,
            SubjectHash = record.SubjectHash,
            Scope = record.Scope,
            CodeHash = record.CodeHash,
            OriginHash = record.OriginHash,
            Attempts = record.Attempts,
            CreatedAt = record.CreatedAt,
            ExpiresAt = record.ExpiresAt,
            ConsumedAt = record.IsConsumed ? record.CreatedAt : null,
        };
        Codes.Add(row);
        await context.SaveChangesAsync(cancellationToken);
        context.Entry(row).State = EntityState.Detached;
    }

    /// <inheritdoc />
    public async Task<OtpRecord?> FindLatestPendingAsync(string subjectHash, string scope, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        var row = await Codes.AsNoTracking()
            .Where(c => c.SubjectHash == subjectHash && c.Scope == scope && c.ConsumedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.ExpiresAt)
            .ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : RecordOf(row);
    }

    /// <inheritdoc />
    public Task RetirePendingAsync(string subjectHash, string scope, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        return Codes
            .Where(c => c.SubjectHash == subjectHash && c.Scope == scope && c.ConsumedAt == null && c.ExpiresAt > at)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.ExpiresAt, at), cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> CountFailedAttemptsAsync(string subjectHash, string scope, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        // A spent code counted its successful attempt too.
        return Codes.AsNoTracking()
            .Where(c => c.SubjectHash == subjectHash && c.Scope == scope && c.CreatedAt >= since)
            .SumAsync(c => c.ConsumedAt == null ? c.Attempts : c.Attempts - 1, cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid recordId, CancellationToken cancellationToken = default)
        => Codes.Where(c => c.Id == recordId).ExecuteDeleteAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TryCountAttemptAsync(Guid recordId, int maxAttempts, CancellationToken cancellationToken = default)
        => await Codes
            .Where(c => c.Id == recordId && c.ConsumedAt == null && c.Attempts < maxAttempts)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.Attempts, c => c.Attempts + 1), cancellationToken) == 1;

    /// <inheritdoc />
    public async Task<bool> TryConsumeAsync(Guid recordId, CancellationToken cancellationToken = default)
    {
        var at = time.GetUtcNow();
        return await Codes
            .Where(c => c.Id == recordId && c.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.ConsumedAt, at), cancellationToken) == 1;
    }

    private static OtpRecord RecordOf(IdentityOtpCode row) => new()
    {
        Id = row.Id,
        SubjectHash = row.SubjectHash,
        CodeHash = row.CodeHash,
        OriginHash = row.OriginHash,
        Scope = row.Scope,
        CreatedAt = row.CreatedAt,
        ExpiresAt = row.ExpiresAt,
        Attempts = row.Attempts,
        IsConsumed = row.ConsumedAt is not null,
    };
}

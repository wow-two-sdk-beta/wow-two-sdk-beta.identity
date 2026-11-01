using Microsoft.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.OneTimeCodes;

/// <summary>
/// Accesses one-time codes in <c>identity_otp_codes</c> through <see cref="IdentityDbContext"/>, so every instance on the
/// schema shares them. Counting an attempt and consuming are single conditional updates; saving prunes rows an hour past
/// their expiry.
/// </summary>
/// <param name="context">The identity context.</param>
/// <param name="time">The clock pruning measures against.</param>
public sealed class EfOtpRepository(IdentityDbContext context, TimeProvider time) : IOtpRepository
{
    private static readonly TimeSpan PruneGrace = TimeSpan.FromHours(1);

    private DbSet<IdentityOtpCode> Codes => context.Set<IdentityOtpCode>();

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

        var horizon = time.GetUtcNow() - PruneGrace;
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
            .ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : RecordOf(row);
    }

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

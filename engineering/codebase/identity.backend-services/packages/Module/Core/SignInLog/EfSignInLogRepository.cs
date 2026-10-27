using Microsoft.EntityFrameworkCore;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;

/// <summary>Accesses the sign-in log through <see cref="IdentityDbContext"/>; the deletion is one statement, idempotent across replicas.</summary>
/// <param name="context">The identity context.</param>
public sealed class EfSignInLogRepository(IdentityDbContext context) : ISignInLogRepository
{
    private DbSet<IdentitySignInLogEntry> Entries => context.Set<IdentitySignInLogEntry>();

    /// <inheritdoc />
    public async Task<long> AddAsync(IdentitySignInLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entries.Add(entry);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return entry.Id;
        }
        finally
        {
            // Detached, written or not, so a later save in the same request never writes the row again.
            context.Entry(entry).State = EntityState.Detached;
        }
    }

    /// <inheritdoc />
    public Task SetSessionAsync(long entryId, Guid sessionId, CancellationToken cancellationToken = default)
        => Entries.Where(e => e.Id == entryId).ExecuteUpdateAsync(setters => setters.SetProperty(e => e.SessionId, sessionId), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IdentitySignInLogEntry>> ListForUserAsync(Guid userId, int take, CancellationToken cancellationToken = default)
        => await Entries.AsNoTracking().Where(e => e.UserId == userId).OrderByDescending(e => e.Id).Take(take).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<int> DeleteBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken = default)
        => Entries.Where(e => e.OccurredAt < before).ExecuteDeleteAsync(cancellationToken);
}

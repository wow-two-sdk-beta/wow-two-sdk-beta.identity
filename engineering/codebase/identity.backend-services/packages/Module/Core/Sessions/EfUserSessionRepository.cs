using Microsoft.EntityFrameworkCore;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;

/// <summary>Accesses sessions through <see cref="IdentityDbContext"/>; touches and revocations are conditional updates.</summary>
/// <param name="context">The identity context.</param>
public sealed class EfUserSessionRepository(IdentityDbContext context) : IUserSessionRepository
{
    private DbSet<IdentitySession> Sessions => context.Set<IdentitySession>();

    /// <inheritdoc />
    public async Task AddAsync(IdentitySession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        Sessions.Add(session);
        await context.SaveChangesAsync(cancellationToken);
        context.Entry(session).State = EntityState.Detached;
    }

    /// <inheritdoc />
    public Task<IdentitySession?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IdentitySession>> ListUnrevokedAsync(Guid userId, DateTimeOffset at, CancellationToken cancellationToken = default)
        => await Sessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > at)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task TouchAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default)
        => Sessions.Where(s => s.Id == id && s.LastSeenAt < at).ExecuteUpdateAsync(setters => setters.SetProperty(s => s.LastSeenAt, at), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IdentitySession>> RevokeAsync(IReadOnlyCollection<Guid> ids, DateTimeOffset at, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
            return [];

        var revoked = await Sessions.AsNoTracking().Where(s => ids.Contains(s.Id) && s.RevokedAt == null).ToListAsync(cancellationToken);
        if (revoked.Count == 0)
            return [];

        // Two concurrent revocations may both report a session; what runs after a revocation is idempotent.
        var revokedIds = revoked.Select(s => s.Id).ToList();
        await Sessions
            .Where(s => revokedIds.Contains(s.Id) && s.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RevokedAt, at).SetProperty(s => s.RevokeReason, reason), cancellationToken);
        foreach (var session in revoked)
        {
            session.RevokedAt = at;
            session.RevokeReason = reason;
        }

        return revoked;
    }
}

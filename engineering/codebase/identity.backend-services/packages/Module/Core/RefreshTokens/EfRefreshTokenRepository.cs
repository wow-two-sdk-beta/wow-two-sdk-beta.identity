using Microsoft.EntityFrameworkCore;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.RefreshTokens;

/// <summary>Accesses refresh tokens through <see cref="IdentityDbContext"/>; consumption is one conditional update.</summary>
/// <param name="context">The identity context.</param>
public sealed class EfRefreshTokenRepository(IdentityDbContext context) : IRefreshTokenRepository
{
    private DbSet<IdentityRefreshToken> Tokens => context.Set<IdentityRefreshToken>();

    /// <inheritdoc />
    public async Task AddAsync(IdentityRefreshToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        Tokens.Add(token);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<IdentityRefreshToken?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => Tokens.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TryConsumeAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default)
        => await Tokens
            .Where(t => t.Id == id && t.ConsumedAt == null && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.ConsumedAt, at), cancellationToken) == 1;

    /// <inheritdoc />
    public Task RevokeFamilyAsync(Guid familyId, DateTimeOffset at, CancellationToken cancellationToken = default)
        => Tokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, at), cancellationToken);

    /// <inheritdoc />
    public Task RevokeUserAsync(Guid userId, DateTimeOffset at, CancellationToken cancellationToken = default)
        => Tokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, at), cancellationToken);

    /// <inheritdoc />
    public Task<int> DeleteExpiredAsync(DateTimeOffset before, CancellationToken cancellationToken = default)
        => Tokens.Where(t => t.ExpiresAt <= before).ExecuteDeleteAsync(cancellationToken);
}

using Microsoft.EntityFrameworkCore;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Passkeys;

/// <summary>Accesses passkeys through <see cref="IdentityDbContext"/>.</summary>
/// <param name="context">The identity context.</param>
public sealed class EfUserPasskeyRepository(IdentityDbContext context) : IUserPasskeyRepository
{
    private DbSet<IdentityPasskey> Passkeys => context.Set<IdentityPasskey>();

    /// <inheritdoc />
    public async Task AddAsync(IdentityPasskey passkey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(passkey);
        Passkeys.Add(passkey);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<IdentityPasskey?> FindAsync(byte[] credentialId, CancellationToken cancellationToken = default)
        => Passkeys.AsNoTracking().FirstOrDefaultAsync(p => p.CredentialId == credentialId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IdentityPasskey>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
        => await Passkeys.AsNoTracking().Where(p => p.UserId == userId).OrderBy(p => p.CreatedAt).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> RecordUseAsync(Guid id, uint signCount, DateTimeOffset ceremonyIssuedAt, DateTimeOffset usedAt, CancellationToken cancellationToken = default)
        => await Passkeys.Where(p => p.Id == id && (p.LastUsedAt == null || p.LastUsedAt < ceremonyIssuedAt))
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.SignCount, signCount).SetProperty(p => p.LastUsedAt, usedAt), cancellationToken) == 1;

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(Guid userId, Guid id, CancellationToken cancellationToken = default)
        => await Passkeys.Where(p => p.Id == id && p.UserId == userId).ExecuteDeleteAsync(cancellationToken) == 1;
}

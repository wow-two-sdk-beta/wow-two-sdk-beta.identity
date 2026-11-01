using Microsoft.EntityFrameworkCore;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.ExternalAccounts;

/// <summary>Accesses external accounts through <see cref="IdentityDbContext"/>.</summary>
/// <param name="context">The identity context.</param>
public sealed class EfExternalAccountRepository(IdentityDbContext context) : IExternalAccountRepository
{
    private DbSet<IdentityExternalAccount> Accounts => context.Set<IdentityExternalAccount>();

    /// <inheritdoc />
    public async Task AddAsync(IdentityExternalAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        Accounts.Add(account);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(Guid userId, string provider, string providerKey, CancellationToken cancellationToken = default)
    {
        var account = await FindAsync(provider, providerKey, cancellationToken);
        if (account is null || account.UserId != userId)
            return false;

        Accounts.Remove(account);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<IdentityExternalAccount?> FindAsync(string provider, string providerKey, CancellationToken cancellationToken = default)
        => await Accounts.FindAsync([provider, providerKey], cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IdentityExternalAccount>> GetAsync(Guid userId, CancellationToken cancellationToken = default)
        => await Accounts.Where(a => a.UserId == userId).OrderBy(a => a.Provider).ToListAsync(cancellationToken);
}

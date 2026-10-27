using Microsoft.EntityFrameworkCore;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Logins;

/// <summary>Accesses external logins through <see cref="IdentityDbContext"/>.</summary>
/// <param name="context">The identity context.</param>
public sealed class EfUserLoginRepository(IdentityDbContext context) : IUserLoginRepository
{
    private DbSet<IdentityUserLogin> Logins => context.Set<IdentityUserLogin>();

    /// <inheritdoc />
    public async Task AddAsync(IdentityUserLogin login, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(login);
        Logins.Add(login);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(Guid userId, string loginProvider, string providerKey, CancellationToken cancellationToken = default)
    {
        var login = await FindAsync(loginProvider, providerKey, cancellationToken);
        if (login is null || login.UserId != userId)
            return false;

        Logins.Remove(login);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<IdentityUserLogin?> FindAsync(string loginProvider, string providerKey, CancellationToken cancellationToken = default)
        => await Logins.FindAsync([loginProvider, providerKey], cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IdentityUserLogin>> GetAsync(Guid userId, CancellationToken cancellationToken = default)
        => await Logins.Where(l => l.UserId == userId).OrderBy(l => l.LoginProvider).ToListAsync(cancellationToken);
}

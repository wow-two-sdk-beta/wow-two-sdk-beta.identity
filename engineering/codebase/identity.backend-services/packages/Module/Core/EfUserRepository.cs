using Microsoft.EntityFrameworkCore;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>Accesses user entities through the Module's <see cref="IdentityDbContext"/>.</summary>
public sealed class EfUserRepository : IUserRepository
{
    private readonly IdentityDbContext _context;

    /// <summary>Create the store over <paramref name="context"/>.</summary>
    /// <param name="context">The identity context.</param>
    public EfUserRepository(IdentityDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    private DbSet<IdentityUser> Users => _context.Set<IdentityUser>();

    /// <inheritdoc />
    public async Task CreateAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var entry = _context.Entry(user);
        if (entry.State == EntityState.Detached)
        {
            var tracked = _context.ChangeTracker
                .Entries<IdentityUser>()
                .FirstOrDefault(candidate =>
                    candidate.State != EntityState.Detached
                    && candidate.Entity.Id == user.Id);

            if (tracked is not null)
            {
                throw new InvalidOperationException(
                    $"Cannot update a detached '{typeof(IdentityUser).Name}' while another instance with key '{user.Id}' is tracked. "
                    + "Apply accepted changes to the tracked instance.");
            }

            Users.Update(user);
        }

        // Attach first: a detached user's loaded stamp becomes the original value the concurrency check compares.
        user.ConcurrencyStamp = Guid.NewGuid().ToString();
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var userId = user.Id;

        // The schema has no foreign keys, so the user's relation rows leave in the same save.
        _context.RemoveRange(await _context.Set<IdentityUserRole>().Where(r => r.UserId == userId).ToListAsync(cancellationToken));
        _context.RemoveRange(await _context.Set<IdentityUserClaim>().Where(c => c.UserId == userId).ToListAsync(cancellationToken));
        _context.RemoveRange(await _context.Set<IdentityUserLogin>().Where(l => l.UserId == userId).ToListAsync(cancellationToken));
        _context.RemoveRange(await _context.Set<IdentityUserToken>().Where(t => t.UserId == userId).ToListAsync(cancellationToken));
        _context.RemoveRange(await _context.Set<IdentityRefreshToken>().Where(t => t.UserId == userId).ToListAsync(cancellationToken));
        Users.Remove(user);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IdentityUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => await Users.FindAsync([userId], cancellationToken);

    /// <inheritdoc />
    public Task<IdentityUser?> FindByNormalizedUserNameAsync(string normalizedUserName, CancellationToken cancellationToken = default)
        => Users.FirstOrDefaultAsync(u => u.NormalizedUserName == normalizedUserName, cancellationToken);

    /// <inheritdoc />
    public Task<IdentityUser?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
        => Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

    /// <inheritdoc />
    public Task<IdentityUser?> FindByConfirmedPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
        => Users.FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber && u.PhoneNumberConfirmed, cancellationToken);
}

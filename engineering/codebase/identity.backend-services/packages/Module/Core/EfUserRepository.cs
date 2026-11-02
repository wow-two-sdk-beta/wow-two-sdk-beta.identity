using Microsoft.EntityFrameworkCore;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>Accesses user entities through the Module's <see cref="IdentityDbContext"/>.</summary>
/// <remarks>Every lookup sees live accounts only: the context hides rows whose <see cref="IdentityUser.DeletedAt"/> is set.</remarks>
public sealed class EfUserRepository : IUserRepository
{
    private readonly IdentityDbContext _context;
    private readonly TimeProvider _time;

    /// <summary>Create the store over <paramref name="context"/>.</summary>
    /// <param name="context">The identity context.</param>
    /// <param name="time">The clock a deletion stamps <see cref="IdentityUser.DeletedAt"/> from.</param>
    public EfUserRepository(IdentityDbContext context, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(time);
        _context = context;
        _time = time;
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
    /// <remarks>
    /// A soft delete: the row stays with <see cref="IdentityUser.DeletedAt"/> set, so keys into it hold, and leaves every
    /// lookup; its role, claim, external-account, stored-token and refresh-token rows leave in the same save, freeing its
    /// external accounts. The user is detached afterwards, as a removed entity would be.
    /// </remarks>
    public async Task DeleteAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var userId = user.Id;

        // The schema has no foreign keys, so the user's relation rows leave in the same save.
        _context.RemoveRange(await _context.Set<IdentityUserRole>().Where(r => r.UserId == userId).ToListAsync(cancellationToken));
        _context.RemoveRange(await _context.Set<IdentityUserClaim>().Where(c => c.UserId == userId).ToListAsync(cancellationToken));
        _context.RemoveRange(await _context.Set<IdentityExternalAccount>().Where(l => l.UserId == userId).ToListAsync(cancellationToken));
        _context.RemoveRange(await _context.Set<IdentityUserToken>().Where(t => t.UserId == userId).ToListAsync(cancellationToken));
        _context.RemoveRange(await _context.Set<IdentityRefreshToken>().Where(t => t.UserId == userId).ToListAsync(cancellationToken));

        // Attach first, as an update does: a detached user's loaded stamp becomes the original value the concurrency check compares.
        if (_context.Entry(user).State == EntityState.Detached)
            Users.Attach(user);
        user.DeletedAt ??= _time.GetUtcNow();
        user.ConcurrencyStamp = Guid.NewGuid().ToString();
        await _context.SaveChangesAsync(cancellationToken);
        _context.Entry(user).State = EntityState.Detached;
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
        => Users.FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber && u.IsPhoneNumberConfirmed, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Two conditional updates: the increment, then the lock of a row whose count reached the limit, which only one
    /// concurrent attempt can still find. Neither touches the concurrency stamp, so a parallel request's save of the same
    /// account is not refused.
    /// </remarks>
    public async Task<bool> CountFailedAttemptAsync(IdentityUser user, int maxFailedAttempts, DateTimeOffset lockoutEnd, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var userId = user.Id;
        var isCounted = await Users
            .Where(u => u.Id == userId && u.IsLockoutEnabled)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.AccessFailedCount, u => u.AccessFailedCount + 1), cancellationToken) == 1;
        if (!isCounted)
            return false;

        var isLocked = await Users
            .Where(u => u.Id == userId && u.AccessFailedCount >= maxFailedAttempts)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.AccessFailedCount, 0)
                .SetProperty(u => u.LockoutEnd, lockoutEnd), cancellationToken) == 1;
        await ReloadLockoutAsync(user, cancellationToken);
        return isLocked;
    }

    /// <inheritdoc />
    public async Task ResetFailedAttemptsAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var userId = user.Id;
        await Users
            .Where(u => u.Id == userId && u.AccessFailedCount != 0)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.AccessFailedCount, 0), cancellationToken);
        await ReloadLockoutAsync(user, cancellationToken);
    }

    /// <summary>Copy the stored count and lockout end onto <paramref name="user"/> as unchanged values, so no later save writes them back.</summary>
    private async Task ReloadLockoutAsync(IdentityUser user, CancellationToken cancellationToken)
    {
        var userId = user.Id;
        var stored = await Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.AccessFailedCount, u.LockoutEnd })
            .FirstOrDefaultAsync(cancellationToken);
        if (stored is null)
            return;

        var entry = _context.Entry(user);
        if (entry.State == EntityState.Detached)
        {
            user.AccessFailedCount = stored.AccessFailedCount;
            user.LockoutEnd = stored.LockoutEnd;
            return;
        }

        var count = entry.Property(u => u.AccessFailedCount);
        count.CurrentValue = count.OriginalValue = stored.AccessFailedCount;
        count.IsModified = false;
        var end = entry.Property(u => u.LockoutEnd);
        end.CurrentValue = end.OriginalValue = stored.LockoutEnd;
        end.IsModified = false;
    }
}

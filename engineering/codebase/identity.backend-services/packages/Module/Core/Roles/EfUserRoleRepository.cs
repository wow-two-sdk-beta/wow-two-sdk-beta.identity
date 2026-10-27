using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;

/// <summary>Accesses role memberships through <see cref="IdentityDbContext"/>.</summary>
/// <param name="context">The identity context.</param>
public sealed class EfUserRoleRepository(IdentityDbContext context) : IUserRoleRepository
{
    private DbSet<IdentityUserRole> Memberships => context.Set<IdentityUserRole>();

    /// <inheritdoc />
    public async Task AddAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default)
    {
        Memberships.Add(new IdentityUserRole { UserId = userId, RoleId = roleId });
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default)
    {
        context.RemoveRange(await Memberships.Where(m => m.UserId == userId && m.RoleId == roleId).ToListAsync(cancellationToken));
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> ContainsAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default)
        => Memberships.AnyAsync(m => m.UserId == userId && m.RoleId == roleId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetRoleNamesAsync(Guid userId, CancellationToken cancellationToken = default)
        => await (from membership in Memberships
                  join role in context.Set<IdentityRole>() on membership.RoleId equals role.Id
                  where membership.UserId == userId && role.Name != null
                  orderby role.Name
                  select role.Name!).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Claim>> GetRoleClaimsAsync(Guid userId, CancellationToken cancellationToken = default)
        => await (from membership in Memberships
                  join claim in context.Set<IdentityRoleClaim>() on membership.RoleId equals claim.RoleId
                  where membership.UserId == userId && claim.ClaimType != null
                  select new Claim(claim.ClaimType!, claim.ClaimValue ?? string.Empty)).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetUserIdsAsync(Guid roleId, CancellationToken cancellationToken = default)
        => await Memberships.Where(m => m.RoleId == roleId).Select(m => m.UserId).ToListAsync(cancellationToken);
}

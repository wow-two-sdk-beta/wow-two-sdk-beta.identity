using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;

/// <summary>Accesses roles and role claims through <see cref="IdentityDbContext"/>.</summary>
/// <param name="context">The identity context.</param>
public sealed class EfRoleRepository(IdentityDbContext context) : IRoleRepository
{
    private DbSet<IdentityRole> Roles => context.Set<IdentityRole>();

    private DbSet<IdentityRoleClaim> RoleClaims => context.Set<IdentityRoleClaim>();

    /// <inheritdoc />
    public async Task CreateAsync(IdentityRole role, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        Roles.Add(role);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(IdentityRole role, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (context.Entry(role).State == EntityState.Detached)
            Roles.Update(role);

        role.ConcurrencyStamp = Guid.NewGuid().ToString();
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(IdentityRole role, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        var roleId = role.Id;
        context.RemoveRange(await RoleClaims.Where(c => c.RoleId == roleId).ToListAsync(cancellationToken));
        context.RemoveRange(await context.Set<IdentityUserRole>().Where(r => r.RoleId == roleId).ToListAsync(cancellationToken));
        Roles.Remove(role);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IdentityRole?> FindByIdAsync(Guid roleId, CancellationToken cancellationToken = default)
        => await Roles.FindAsync([roleId], cancellationToken);

    /// <inheritdoc />
    public Task<IdentityRole?> FindByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken = default)
        => Roles.FirstOrDefaultAsync(r => r.NormalizedName == normalizedName, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Claim>> GetClaimsAsync(Guid roleId, CancellationToken cancellationToken = default)
        => await RoleClaims
            .Where(c => c.RoleId == roleId)
            .Select(c => new Claim(c.ClaimType!, c.ClaimValue ?? string.Empty))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddClaimAsync(Guid roleId, Claim claim, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        RoleClaims.Add(new IdentityRoleClaim { RoleId = roleId, ClaimType = claim.Type, ClaimValue = claim.Value });
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveClaimAsync(Guid roleId, Claim claim, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        context.RemoveRange(await RoleClaims
            .Where(c => c.RoleId == roleId && c.ClaimType == claim.Type && c.ClaimValue == claim.Value)
            .ToListAsync(cancellationToken));
        await context.SaveChangesAsync(cancellationToken);
    }
}

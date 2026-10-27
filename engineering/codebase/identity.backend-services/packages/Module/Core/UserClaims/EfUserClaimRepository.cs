using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.UserClaims;

/// <summary>Accesses per-user claims through <see cref="IdentityDbContext"/>.</summary>
/// <param name="context">The identity context.</param>
public sealed class EfUserClaimRepository(IdentityDbContext context) : IUserClaimRepository
{
    private DbSet<IdentityUserClaim> Claims => context.Set<IdentityUserClaim>();

    /// <inheritdoc />
    public async Task<IReadOnlyList<Claim>> GetAsync(Guid userId, CancellationToken cancellationToken = default)
        => await Claims
            .Where(c => c.UserId == userId && c.ClaimType != null)
            .OrderBy(c => c.Id)
            .Select(c => new Claim(c.ClaimType!, c.ClaimValue ?? string.Empty))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(Guid userId, IEnumerable<Claim> claims, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claims);
        Claims.AddRange(claims.Select(claim => new IdentityUserClaim { UserId = userId, ClaimType = claim.Type, ClaimValue = claim.Value }));
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> RemoveAsync(Guid userId, Claim claim, CancellationToken cancellationToken = default)
    {
        var matches = await MatchesAsync(userId, claim, cancellationToken);
        context.RemoveRange(matches);
        await context.SaveChangesAsync(cancellationToken);
        return matches.Count;
    }

    /// <inheritdoc />
    public async Task<int> ReplaceAsync(Guid userId, Claim claim, Claim replacement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var matches = await MatchesAsync(userId, claim, cancellationToken);
        foreach (var match in matches)
        {
            match.ClaimType = replacement.Type;
            match.ClaimValue = replacement.Value;
        }

        await context.SaveChangesAsync(cancellationToken);
        return matches.Count;
    }

    private Task<List<IdentityUserClaim>> MatchesAsync(Guid userId, Claim claim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        return Claims
            .Where(c => c.UserId == userId && c.ClaimType == claim.Type && c.ClaimValue == claim.Value)
            .ToListAsync(cancellationToken);
    }
}

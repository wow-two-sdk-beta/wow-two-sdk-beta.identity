using Microsoft.EntityFrameworkCore;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;

/// <summary>Implements <see cref="IInvitationRepository"/> over <see cref="IdentityDbContext"/>; redemption and withdrawal are conditional updates.</summary>
/// <param name="context">The identity context.</param>
public sealed class EfInvitationRepository(IdentityDbContext context) : IInvitationRepository
{
    private DbSet<IdentityInvitation> Invitations => context.Set<IdentityInvitation>();

    /// <inheritdoc />
    public async Task AddAsync(IdentityInvitation invitation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        Invitations.Add(invitation);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<IdentityInvitation?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        => Invitations.AsNoTracking().FirstOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IdentityInvitation>> ListAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => await Invitations.AsNoTracking().Where(i => i.OrganizationId == organizationId).OrderByDescending(i => i.CreatedAt).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TryAcceptAsync(Guid id, Guid userId, DateTimeOffset at, CancellationToken cancellationToken = default)
        => await Invitations
            .Where(i => i.Id == id && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > at)
            .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.AcceptedAt, at).SetProperty(i => i.AcceptedByUserId, userId), cancellationToken) == 1;

    /// <inheritdoc />
    public async Task<bool> TryRevokeAsync(Guid organizationId, Guid id, DateTimeOffset at, CancellationToken cancellationToken = default)
        => await Invitations
            .Where(i => i.Id == id && i.OrganizationId == organizationId && i.AcceptedAt == null && i.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.RevokedAt, at), cancellationToken) == 1;
}

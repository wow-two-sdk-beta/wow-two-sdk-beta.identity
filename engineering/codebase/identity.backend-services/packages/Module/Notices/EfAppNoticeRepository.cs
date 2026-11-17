using Microsoft.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Identity.Core;

namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>Accesses <c>identity_app_notices</c> and <c>identity_notice_mutes</c> through <see cref="IdentityDbContext"/>.</summary>
/// <param name="context">The identity context.</param>
public sealed class EfAppNoticeRepository(IdentityDbContext context) : IAppNoticeRepository
{
    private DbSet<IdentityAppNotice> Requests => context.Set<IdentityAppNotice>();

    private DbSet<IdentityNoticeMute> Mutes => context.Set<IdentityNoticeMute>();

    /// <inheritdoc />
    public async Task<AppNoticeClaim> ClaimAsync(IdentityAppNotice request, DateTimeOffset staleBefore, DateTimeOffset expiredBefore, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await Requests.Where(r => r.CreatedAt < expiredBefore).ExecuteDeleteAsync(cancellationToken);

        // Two tries: the row holding the key may leave between the failed insert and the read (a released claim).
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (await TryInsertAsync(request, cancellationToken))
                return new AppNoticeClaim(request.Id, null);

            var existing = await Requests.AsNoTracking()
                .FirstOrDefaultAsync(r => r.ClientId == request.ClientId && r.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (existing is null)
                continue;

            if (existing.Status == IdentityAppNotice.PendingStatus && existing.CreatedAt < staleBefore && existing.RequestHash == request.RequestHash)
            {
                var taken = await Requests
                    .Where(r => r.Id == existing.Id && r.Status == IdentityAppNotice.PendingStatus && r.CreatedAt == existing.CreatedAt)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.CreatedAt, request.CreatedAt), cancellationToken);
                if (taken == 1)
                    return new AppNoticeClaim(existing.Id, null);
            }

            return new AppNoticeClaim(null, existing);
        }

        throw new InvalidOperationException("The app notice's idempotency key could be neither claimed nor read.");
    }

    /// <inheritdoc />
    public async Task CompleteAsync(Guid id, string status, Guid? messageRecordId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        await Requests.Where(r => r.Id == id).ExecuteUpdateAsync(
            setters => setters.SetProperty(r => r.Status, status).SetProperty(r => r.MessageRecordId, messageRecordId),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(Guid id, CancellationToken cancellationToken = default)
        => await Requests.Where(r => r.Id == id && r.Status == IdentityAppNotice.PendingStatus).ExecuteDeleteAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsMutedAsync(Guid userId, string clientId, CancellationToken cancellationToken = default)
        => Mutes.AnyAsync(m => m.UserId == userId && (m.ClientId == clientId || m.ClientId == IdentityNoticeMute.AllClients), cancellationToken);

    /// <inheritdoc />
    public async Task MuteAsync(Guid userId, string clientId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        if (await Mutes.AnyAsync(m => m.UserId == userId && m.ClientId == clientId, cancellationToken))
            return;

        var mute = new IdentityNoticeMute { UserId = userId, ClientId = clientId, MutedAt = at };
        Mutes.Add(mute);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another press muted it first; muting twice changes nothing.
        }
        finally
        {
            context.Entry(mute).State = EntityState.Detached;
        }
    }

    /// <inheritdoc />
    public async Task<int> UnmuteAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        return userIds.Count == 0 ? 0 : await Mutes.Where(m => userIds.Contains(m.UserId)).ExecuteDeleteAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAccountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await Mutes.Where(m => m.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await Requests.Where(r => r.UserId == userId).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<bool> TryInsertAsync(IdentityAppNotice request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
        finally
        {
            // Saved or not, the row leaves the context, so a failed write never rides along with the scope's next save.
            context.Entry(request).State = EntityState.Detached;
        }
    }
}

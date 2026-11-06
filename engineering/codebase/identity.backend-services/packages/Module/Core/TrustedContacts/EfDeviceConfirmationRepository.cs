using Microsoft.EntityFrameworkCore;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>Accesses <c>identity_known_devices</c> and <c>identity_device_confirmations</c> through <see cref="IdentityDbContext"/>.</summary>
/// <param name="context">The identity context.</param>
public sealed class EfDeviceConfirmationRepository(IdentityDbContext context) : IDeviceConfirmationRepository
{
    private DbSet<IdentityKnownDevice> Devices => context.Set<IdentityKnownDevice>();

    private DbSet<IdentityDeviceConfirmation> Confirmations => context.Set<IdentityDeviceConfirmation>();

    /// <inheritdoc />
    public Task<bool> IsKnownAsync(Guid userId, string deviceHash, CancellationToken cancellationToken = default)
        => Devices.AsNoTracking().AnyAsync(d => d.UserId == userId && d.DeviceHash == deviceHash, cancellationToken);

    /// <inheritdoc />
    public async Task RememberAsync(IdentityKnownDevice device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (await IsKnownAsync(device.UserId, device.DeviceHash, cancellationToken))
            return;

        Devices.Add(device);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Only another request remembering it first is the same outcome.
            context.Entry(device).State = EntityState.Detached;
            if (!await IsKnownAsync(device.UserId, device.DeviceHash, cancellationToken))
                throw;
            return;
        }

        context.Entry(device).State = EntityState.Detached;
    }

    /// <inheritdoc />
    public async Task ReplaceAsync(IdentityDeviceConfirmation confirmation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        await Confirmations
            .Where(c => (c.UserId == confirmation.UserId && c.DecidedAt == null) || c.ExpiresAt <= confirmation.CreatedAt)
            .ExecuteDeleteAsync(cancellationToken);
        Confirmations.Add(confirmation);
        await context.SaveChangesAsync(cancellationToken);
        context.Entry(confirmation).State = EntityState.Detached;
    }

    /// <inheritdoc />
    public Task<IdentityDeviceConfirmation?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => Confirmations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<IdentityDeviceConfirmation?> FindByTokenAsync(string tokenHash, CancellationToken cancellationToken = default)
        => Confirmations.AsNoTracking().FirstOrDefaultAsync(c => c.TokenHash == tokenHash, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TryDecideAsync(Guid id, Guid contactUserId, bool isApproved, DateTimeOffset decidedAt, CancellationToken cancellationToken = default)
        => await Confirmations
            .Where(c => c.Id == id && c.DecidedAt == null)
            .ExecuteUpdateAsync(
                row => row
                    .SetProperty(c => c.IsApproved, (bool?)isApproved)
                    .SetProperty(c => c.DecidedByUserId, (Guid?)contactUserId)
                    .SetProperty(c => c.DecidedAt, (DateTimeOffset?)decidedAt),
                cancellationToken) > 0;

    /// <inheritdoc />
    public async Task<bool> TryRemoveAsync(Guid id, CancellationToken cancellationToken = default)
        => await Confirmations.Where(c => c.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;

    /// <inheritdoc />
    public async Task RemoveRequestsOfAsync(Guid userId, CancellationToken cancellationToken = default)
        => await Confirmations.Where(c => c.UserId == userId).ExecuteDeleteAsync(cancellationToken);

    /// <inheritdoc />
    public async Task RemoveAllOfAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await Devices.Where(d => d.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await RemoveRequestsOfAsync(userId, cancellationToken);
    }
}

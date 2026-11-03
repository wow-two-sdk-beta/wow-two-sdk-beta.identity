using Microsoft.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Foundation.Naming;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>Accesses <c>identity_trusted_contacts</c>, and reads roles, sessions and the sign-in log, through <see cref="IdentityDbContext"/>.</summary>
/// <param name="context">The identity context.</param>
public sealed class EfTrustedContactRepository(IdentityDbContext context) : ITrustedContactRepository
{
    private static readonly string[] InstanceRoleNames = [.. InstanceRoleConstants.All.Select(role => role.ToCanonical()!)];

    private static readonly string[] OwnPhoneMethods = [SignInMethodConstants.PhoneCode, SignInMethodConstants.PhoneContact];

    private DbSet<IdentityTrustedContact> Contacts => context.Set<IdentityTrustedContact>();

    /// <inheritdoc />
    public async Task<IReadOnlyList<IdentityTrustedContact>> ListContactsAsync(Guid userId, CancellationToken cancellationToken = default)
        => await Contacts.AsNoTracking().Where(c => c.UserId == userId).OrderBy(c => c.CreatedAt).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IdentityTrustedContact>> ListContactsOfAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count == 0)
            return [];

        return await Contacts.AsNoTracking().Where(c => userIds.Contains(c.UserId)).OrderBy(c => c.CreatedAt).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IdentityTrustedContact>> ListTrustingAsync(Guid contactUserId, CancellationToken cancellationToken = default)
        => await Contacts.AsNoTracking().Where(c => c.ContactUserId == contactUserId).OrderBy(c => c.CreatedAt).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<IdentityTrustedContact?> FindAsync(Guid userId, Guid contactUserId, CancellationToken cancellationToken = default)
        => Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId && c.ContactUserId == contactUserId, cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(IdentityTrustedContact contact, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contact);
        Contacts.Add(contact);
        await context.SaveChangesAsync(cancellationToken);
        context.Entry(contact).State = EntityState.Detached;
    }

    /// <inheritdoc />
    public async Task<bool> TryRemoveAsync(Guid userId, Guid contactUserId, CancellationToken cancellationToken = default)
        => await Contacts.Where(c => c.UserId == userId && c.ContactUserId == contactUserId).ExecuteDeleteAsync(cancellationToken) > 0;

    /// <inheritdoc />
    public Task RemoveAllOfAsync(Guid userId, CancellationToken cancellationToken = default)
        => Contacts.Where(c => c.UserId == userId || c.ContactUserId == userId).ExecuteDeleteAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> HoldsInstanceRoleAsync(Guid userId, CancellationToken cancellationToken = default)
        => context.UserRoles.AsNoTracking()
            .Where(userRole => userRole.UserId == userId)
            .Join(context.Roles.AsNoTracking(), userRole => userRole.RoleId, role => role.Id, (_, role) => role.NormalizedName)
            .AnyAsync(name => name != null && InstanceRoleNames.Contains(name), cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsOwnPhoneSignInAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
        => context.SignInLog.AsNoTracking().AnyAsync(
            entry => entry.UserId == userId
                && entry.SessionId == sessionId
                && entry.Result == SignInResultConstants.Succeeded
                && entry.TrustedContactUserId == null
                && OwnPhoneMethods.Contains(entry.Method),
            cancellationToken);

    /// <inheritdoc />
    public Task<IdentitySession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => context.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
}

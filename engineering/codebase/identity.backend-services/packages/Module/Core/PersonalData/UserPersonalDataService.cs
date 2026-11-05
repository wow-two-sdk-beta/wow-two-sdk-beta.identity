using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Identity.Core.AccessChanges;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;

/// <summary>
/// Provides the personal-data rights of an account: export everything the SDK and the product hold for the user, and
/// erase the account — the product's handlers first, then every identity relation row and the account's personal
/// columns, in one transaction when none is open. Secrets (password hash, tokens, keys) are never exported.
/// </summary>
/// <param name="context">The identity context.</param>
/// <param name="exporters">The product's sections, and roles when that slice is registered.</param>
/// <param name="handlers">The product's deletion handlers.</param>
/// <param name="time">Stamps the export and the erasure.</param>
/// <param name="sessions">The sessions slice, which an erasure ends at once; null without it.</param>
/// <param name="accessChanges">What hears the account left each organization; null tells nobody.</param>
public sealed class UserPersonalDataService(
    IdentityDbContext context,
    IEnumerable<IPersonalDataExporter> exporters,
    IEnumerable<IAccountDeletionHandler> handlers,
    TimeProvider time,
    UserSessionService? sessions = null,
    IEnumerable<IAccessChangeHandler>? accessChanges = null)
{
    /// <summary>Exports the user's identity data and every product section.</summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Two exporters claim one section.</exception>
    public async Task<PersonalDataExportModel> ExportAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var sections = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["account"] = new
            {
                Id = user.Id.ToString(),
                user.UserName,
                user.DisplayName,
                user.Email,
                user.IsEmailConfirmed,
                user.PhoneNumber,
                user.IsPhoneNumberConfirmed,
                user.IsTwoFactorEnabled,
                user.LockoutEnd,
            },
        };
        sections["claims"] = await context.Set<IdentityUserClaim>().AsNoTracking().Where(c => c.UserId == user.Id).Select(c => new { Type = c.ClaimType, Value = c.ClaimValue }).ToListAsync(cancellationToken);
        sections["external_accounts"] = await context.Set<IdentityExternalAccount>().AsNoTracking().Where(a => a.UserId == user.Id).Select(a => new { a.Provider, DisplayName = a.ProviderDisplayName }).ToListAsync(cancellationToken);
        sections["passkeys"] = await context.Set<IdentityPasskey>().AsNoTracking().Where(p => p.UserId == user.Id).Select(p => new { p.Name, p.CreatedAt, p.LastUsedAt }).ToListAsync(cancellationToken);
        sections["sessions"] = await context.Set<IdentityRefreshToken>().AsNoTracking().Where(t => t.UserId == user.Id).Select(t => new { t.CreatedAt, t.ExpiresAt, t.RevokedAt }).ToListAsync(cancellationToken);
        sections["sign_in_sessions"] = await context.Set<IdentitySession>().AsNoTracking().Where(s => s.UserId == user.Id).OrderBy(s => s.CreatedAt)
            .Select(s => new { s.CreatedAt, s.LastSeenAt, s.ExpiresAt, s.RevokedAt, s.AuthenticationMethods, s.IpMasked, s.UserAgent }).ToListAsync(cancellationToken);
        sections["sign_in_log"] = await context.Set<IdentitySignInLogEntry>().AsNoTracking().Where(e => e.UserId == user.Id).OrderBy(e => e.Id)
            .Select(e => new { e.OccurredAt, e.Method, e.Result, e.Reason, e.ClientId, e.IpMasked, e.UserAgent }).ToListAsync(cancellationToken);

        foreach (var exporter in exporters)
        {
            if (!sections.TryAdd(exporter.Section, await exporter.ExportAsync(user, cancellationToken)))
                throw new InvalidOperationException($"Two personal-data exporters claim the section '{exporter.Section}'.");
        }

        return new PersonalDataExportModel { GeneratedAt = time.GetUtcNow(), Sections = sections };
    }

    /// <summary>
    /// Erases the account: the product's handlers, then roles, claims, external accounts, stored tokens, refresh tokens, sign-in
    /// sessions, passkeys and organization memberships leave, and the user row is scrubbed in place — names, email,
    /// phone and password hash cleared, flags reset, the stamps replaced and <see cref="IdentityUser.DeletedAt"/> set
    /// unless it already was. The row stays, so a product's key into <c>identity_users</c> never blocks; it resolves no
    /// more. Each session's end and each membership's removal tell their handlers, so a service that hears refuses the person's
    /// issued tokens at once; one that doesn't accepts them until they expire. Sign-in log rows stay until their retention ends.
    /// </summary>
    /// <remarks>Runs again safely after a failure, and on an account that was soft-deleted before.</remarks>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DeleteAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        foreach (var handler in handlers)
            await handler.HandleAsync(user, cancellationToken);

        // Before the rows go: an ended session stays ended whatever becomes of the rest, and a second run finds none to tell of.
        if (sessions is not null)
            await sessions.RevokeAllAsync(user, keep: null, SessionRevokeReasonConstants.AccountDeleted, cancellationToken);

        var organizations = await context.Set<IdentityMembership>().AsNoTracking().Where(row => row.UserId == user.Id).Select(row => row.OrganizationId).ToListAsync(cancellationToken);
        await using var transaction = context.Database.CurrentTransaction is null && context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await DeleteRowsAsync<IdentityUserRole>(row => row.UserId == user.Id, cancellationToken);
        await DeleteRowsAsync<IdentityUserClaim>(row => row.UserId == user.Id, cancellationToken);
        await DeleteRowsAsync<IdentityExternalAccount>(row => row.UserId == user.Id, cancellationToken);
        await DeleteRowsAsync<IdentityUserToken>(row => row.UserId == user.Id, cancellationToken);
        await DeleteRowsAsync<IdentityRefreshToken>(row => row.UserId == user.Id, cancellationToken);
        await DeleteRowsAsync<IdentitySession>(row => row.UserId == user.Id, cancellationToken);
        await DeleteRowsAsync<IdentityPasskey>(row => row.UserId == user.Id, cancellationToken);
        await DeleteRowsAsync<IdentityMembership>(row => row.UserId == user.Id, cancellationToken);
        await ScrubAsync(user.Id, cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);

        // A tracked copy would still carry the erased values; the next read goes to the scrubbed row.
        if (context.ChangeTracker.Entries<IdentityUser>().FirstOrDefault(entry => entry.Entity.Id == user.Id) is { } tracked)
            tracked.State = EntityState.Detached;

        foreach (var organization in organizations)
            await TellRemovedAsync(user.Id, organization, cancellationToken);
    }

    /// <summary>Tells every handler the account no longer belongs to <paramref name="organizationId"/>.</summary>
    private async Task TellRemovedAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken)
    {
        var change = new AccessChangedEvent
        {
            Subject = userId.ToString("D", CultureInfo.InvariantCulture),
            OrganizationId = organizationId.ToString("D", CultureInfo.InvariantCulture),
            Kind = AccessChangeKind.Removed,
            At = time.GetUtcNow(),
        };
        foreach (var handler in accessChanges ?? [])
            await handler.HandleAsync(change, cancellationToken);
    }

    /// <summary>Clears the account's personal columns in place, deleted rows included; the row and its id stay.</summary>
    private Task<int> ScrubAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var securityStamp = Guid.NewGuid().ToString("N");
        var concurrencyStamp = Guid.NewGuid().ToString();
        return context.Set<IdentityUser>()
            .IgnoreQueryFilters()
            .Where(row => row.Id == userId)
            .ExecuteUpdateAsync(
                row => row
                    .SetProperty(u => u.UserName, (string?)null)
                    .SetProperty(u => u.NormalizedUserName, (string?)null)
                    .SetProperty(u => u.DisplayName, (string?)null)
                    .SetProperty(u => u.Email, (string?)null)
                    .SetProperty(u => u.NormalizedEmail, (string?)null)
                    .SetProperty(u => u.IsEmailConfirmed, false)
                    .SetProperty(u => u.PasswordHash, (string?)null)
                    .SetProperty(u => u.SecurityStamp, securityStamp)
                    .SetProperty(u => u.ConcurrencyStamp, concurrencyStamp)
                    .SetProperty(u => u.PhoneNumber, (string?)null)
                    .SetProperty(u => u.IsPhoneNumberConfirmed, false)
                    .SetProperty(u => u.IsTwoFactorEnabled, false)
                    .SetProperty(u => u.LockoutEnd, (DateTimeOffset?)null)
                    .SetProperty(u => u.AccessFailedCount, 0)
                    .SetProperty(u => u.UpdatedAt, now)
                    .SetProperty(u => u.DeletedAt, u => u.DeletedAt ?? now),
                cancellationToken);
    }

    private Task<int> DeleteRowsAsync<TRow>(System.Linq.Expressions.Expression<Func<TRow, bool>> owned, CancellationToken cancellationToken)
        where TRow : class
        => context.Set<TRow>().Where(owned).ExecuteDeleteAsync(cancellationToken);
}

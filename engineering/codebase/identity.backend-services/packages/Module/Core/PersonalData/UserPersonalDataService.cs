using Microsoft.EntityFrameworkCore;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;

/// <summary>
/// Provides the personal-data rights of an account: export everything the SDK and the product hold for the user, and
/// delete the account — the product's handlers first, then every identity row, in one transaction when none is open.
/// Secrets (password hash, tokens, keys) are never exported.
/// </summary>
/// <param name="context">The identity context.</param>
/// <param name="exporters">The product's sections, and roles when that slice is registered.</param>
/// <param name="handlers">The product's deletion handlers.</param>
/// <param name="time">Stamps the export.</param>
public sealed class UserPersonalDataService(
    IdentityDbContext context,
    IEnumerable<IPersonalDataExporter> exporters,
    IEnumerable<IAccountDeletionHandler> handlers,
    TimeProvider time)
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
                user.Email,
                user.EmailConfirmed,
                user.PhoneNumber,
                user.PhoneNumberConfirmed,
                user.TwoFactorEnabled,
                user.LockoutEnd,
            },
        };
        sections["claims"] = await context.Set<IdentityUserClaim>().AsNoTracking().Where(c => c.UserId == user.Id).Select(c => new { Type = c.ClaimType, Value = c.ClaimValue }).ToListAsync(cancellationToken);
        sections["logins"] = await context.Set<IdentityUserLogin>().AsNoTracking().Where(l => l.UserId == user.Id).Select(l => new { Provider = l.LoginProvider, DisplayName = l.ProviderDisplayName }).ToListAsync(cancellationToken);
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
    /// Deletes the account: the product's handlers, then roles, claims, logins, stored tokens, refresh tokens, sign-in
    /// sessions, passkeys, organization memberships and the user row. Issued access tokens live until they expire; sign-in
    /// log rows stay until their retention ends, naming an id that no longer resolves.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DeleteAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        foreach (var handler in handlers)
            await handler.HandleAsync(user, cancellationToken);

        await using var transaction = context.Database.CurrentTransaction is null && context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await DeleteRowsAsync<IdentityUserRole>(row => row.UserId == user.Id, cancellationToken);
        await DeleteRowsAsync<IdentityUserClaim>(row => row.UserId == user.Id, cancellationToken);
        await DeleteRowsAsync<IdentityUserLogin>(row => row.UserId == user.Id, cancellationToken);
        await DeleteRowsAsync<IdentityUserToken>(row => row.UserId == user.Id, cancellationToken);
        await DeleteRowsAsync<IdentityRefreshToken>(row => row.UserId == user.Id, cancellationToken);
        await DeleteRowsAsync<IdentitySession>(row => row.UserId == user.Id, cancellationToken);
        await DeleteRowsAsync<IdentityPasskey>(row => row.UserId == user.Id, cancellationToken);
        await DeleteRowsAsync<IdentityMembership>(row => row.UserId == user.Id, cancellationToken);
        await context.Set<IdentityUser>().Where(row => row.Id == user.Id).ExecuteDeleteAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
    }

    private Task<int> DeleteRowsAsync<TRow>(System.Linq.Expressions.Expression<Func<TRow, bool>> owned, CancellationToken cancellationToken)
        where TRow : class
        => context.Set<TRow>().Where(owned).ExecuteDeleteAsync(cancellationToken);
}

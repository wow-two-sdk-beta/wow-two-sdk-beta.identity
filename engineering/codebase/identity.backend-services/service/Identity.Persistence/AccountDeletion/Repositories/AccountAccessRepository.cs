using Identity.Application.AccountDeletion.Repositories;
using Microsoft.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Identity.Core;

namespace Identity.Persistence.AccountDeletion.Repositories;

/// <inheritdoc />
public sealed class AccountAccessRepository(IdentityDbContext context) : IAccountAccessRepository
{
    /// <inheritdoc />
    public Task<bool> HasOrganizationRoleAsync(Guid personId, CancellationToken cancellationToken) =>
        context.Set<IdentityMembership>().AsNoTracking().AnyAsync(row => row.UserId == personId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> HasInstanceRoleAsync(Guid personId, CancellationToken cancellationToken) =>
        context.Set<IdentityUserRole>().AsNoTracking().AnyAsync(row => row.UserId == personId, cancellationToken);
}

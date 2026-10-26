using Microsoft.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Sqlite;
using WoW.Two.Sdk.Beta.Be.Identity.Core;

namespace Identity.Api.Accounts;

/// <summary>The service's store, built as any Form 1 consumer builds it: the module's identity schema on the host's own context.</summary>
/// <param name="options">The context options, configured by <c>AddIdentityModule</c>.</param>
internal sealed class IdentityAccountsDbContext(DbContextOptions<IdentityAccountsDbContext> options) : AppDbContextBase(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyIdentitySchema<IdentityUser, IdentityRole, Guid>();

        // SQLite has no native DateTimeOffset: store it as a sortable binary long, as the module's own tests do.
        modelBuilder.ApplyDateTimeOffsetToBinaryConversion();
    }
}

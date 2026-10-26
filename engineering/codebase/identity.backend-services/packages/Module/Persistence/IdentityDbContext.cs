using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace WoW.Two.Sdk.Beta.Be.Identity.Module.Persistence;

/// <summary>The Identity module's context — a pure mapper over the SQL-owned schema in <c>Migrations/</c>.</summary>
/// <remarks>Internal: consumers reach the store through <c>IGreetingsBroker</c> only, never its tables.</remarks>
/// <param name="options">The context options, configured by <c>AddIdentity</c>.</param>
internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    /// <summary>Gets the served-greeting log.</summary>
    public DbSet<GreetingLog> GreetingLogs => Set<GreetingLog>();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite cannot ORDER BY a TEXT-stored DateTimeOffset — store it as a sortable binary long instead.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var log = modelBuilder.Entity<GreetingLog>();
        log.ToTable(GreetingLog.TableName);
        log.HasKey(row => row.Id);
        log.Property(row => row.Id).HasColumnName("id");
        log.Property(row => row.Message).HasColumnName("message");
        log.Property(row => row.GreetedAtUtc).HasColumnName("greeted_at_utc");
    }
}

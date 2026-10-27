using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace Identity.Tests.Migrations;

/// <summary>
/// The Module's embedded <c>identity</c> migration set against a real engine: apply → rollback → apply, the applied
/// schema against the <see cref="IdentityDbContext"/> model column for column, and the Module's own registration into a
/// configured schema. Each dialect runs every fact through its subclass.
/// </summary>
public abstract class IdentitySchemaTests : IAsyncLifetime
{
    /// <summary>The default identity schema.</summary>
    protected const string Schema = "identity";

    /// <summary>The engine under test.</summary>
    protected abstract DatabaseProvider Provider { get; }

    /// <summary>A fresh, empty database for this test.</summary>
    protected abstract string ConnectionString { get; }

    /// <inheritdoc />
    public abstract Task InitializeAsync();

    /// <inheritdoc />
    public abstract Task DisposeAsync();

    [Fact]
    public async Task Apply_ShouldCreateTheAccountTables_AndRecordTheSet()
    {
        await using var migrator = CreateMigrator();

        var applied = (await migrator.ApplyAllAsync()).ValueOrThrow();

        applied.Should().ContainSingle().Which.Applied.Should().Equal("001-accounts");
        foreach (var table in SchemaShape.AccountTables)
            (await migrator.HasTableAsync(Schema, table)).Should().BeTrue($"001-accounts creates {table}");
        (await migrator.ReadHistoryAsync(IdentityDatabaseOptions.MigrationSetName)).Select(row => row.Ordinal).Should().Equal(1);
        (await migrator.ApplyAllAsync()).ValueOrThrow().Single().Applied.Should().BeEmpty("an applied set is current");
    }

    [Fact]
    public async Task ApplyRollbackApply_ShouldRebuildTheSchema()
    {
        await using var migrator = CreateMigrator();
        var runner = migrator.RunnerFor(IdentityDatabaseOptions.MigrationSetName);
        (await migrator.ApplyAllAsync()).ValueOrThrow();

        (await runner.RollbackAsync(targetOrdinal: 0)).ValueOrThrow().Should().Equal("001-accounts");
        foreach (var table in SchemaShape.AccountTables)
            (await migrator.HasTableAsync(Schema, table)).Should().BeFalse($"the rollback drops {table}");
        (await migrator.ReadHistoryAsync(IdentityDatabaseOptions.MigrationSetName)).Should().BeEmpty();

        (await migrator.ApplyAllAsync()).ValueOrThrow().Single().Applied.Should().Equal("001-accounts");
        foreach (var table in SchemaShape.AccountTables)
            (await migrator.HasTableAsync(Schema, table)).Should().BeTrue($"the second apply recreates {table}");
    }

    [Fact]
    public async Task AppliedSchema_ShouldMatchTheModel()
    {
        await using var migrator = CreateMigrator();
        (await migrator.ApplyAllAsync()).ValueOrThrow();
        await using var context = SchemaShape.CreateContext(Provider, ConnectionString, Schema);
        await using var connection = await migrator.OpenConnectionAsync();

        var model = SchemaShape.FromModel(context);
        var applied = await SchemaShape.FromDatabaseAsync(connection, Provider, Schema);

        model.Select(table => table.Name).Should().Equal(SchemaShape.AccountTables);
        applied.Should().BeEquivalentTo(model, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task ModuleRegistration_ShouldMigrateAndMapTheConfiguredSchema()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDatabaseSettings(ConnectionString);
        services.AddUserAccounts(configureDatabase: database =>
        {
            database.Provider = Provider;
            database.Schema = "identity_tenant";
        });
        await using var provider = services.BuildServiceProvider();

        var set = provider.GetRequiredService<IMigrationSetRegistry>().GetOptions(IdentityDatabaseOptions.MigrationSetName);
        set.Should().BeEquivalentTo(
            new { Provider, SchemaName = "identity_tenant", SchemaOwner = IdentitySchemaOwnerConstants.Module, TableName = "identity_migration_history" },
            options => options.ExcludingMissingMembers());
        (await provider.ApplyMigrationSetsAsync("test")).ValueOrThrow().Single().Applied.Should().Equal("001-accounts");

        var user = new IdentityUser { UserName = "alice", Email = "alice@example.test" };
        await using (var scope = provider.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<UserAccountService>().CreateAsync(user)).Succeeded.Should().BeTrue();

        await using var next = provider.CreateAsyncScope();
        var found = await next.ServiceProvider.GetRequiredService<UserAccountService>().FindByNameAsync("ALICE");
        found.Should().NotBeNull();
        found!.Id.Should().Be(user.Id);
        found.Id.Version.Should().Be(7);
    }

    /// <summary>A harness running the Module's embedded SQL for <see cref="Provider"/> as the <c>identity</c> set, rollback allowed.</summary>
    private MigratorHarness CreateMigrator() => Provider == DatabaseProvider.Sqlite
        ? MigratorHarness.CreateSqlite(ConnectionString, AddIdentitySet)
        : MigratorHarness.CreatePostgres(ConnectionString, AddIdentitySet);

    private void AddIdentitySet(MigratorSetsBuilder sets) => sets.Add(
        IdentityDatabaseOptions.MigrationSetName,
        _ => new EmbeddedResourceMigrationBroker(
            typeof(IdentityDbContext).Assembly, IdentityDatabaseServiceCollectionExtensions.MigrationResourcePrefix(Provider)),
        options =>
        {
            options.SchemaName = Schema;
            options.SchemaOwner = IdentitySchemaOwnerConstants.Module;
            options.AllowRollback = true;
        });
}

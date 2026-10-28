using AwesomeAssertions;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace Identity.Tests.Migrations;

/// <summary>
/// The Module's embedded <c>identity</c> migration set on PostgreSQL in Testcontainers: apply → rollback → apply, the
/// applied schema against the <see cref="IdentityDbContext"/> model column for column, and the Module's own registration
/// into a configured schema. One container serves the tier; every user schema is dropped before each test.
/// </summary>
/// <param name="fixture">The shared container.</param>
[Collection(PostgresMigratorCollection.Name)]
public sealed class IdentitySchemaTests(MigratorPostgresFixture fixture) : IAsyncLifetime
{
    /// <summary>The default identity schema.</summary>
    internal const string Schema = "identity";

    /// <inheritdoc />
    public async Task InitializeAsync() => await fixture.ResetAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Apply_ShouldCreateEveryTable_AndRecordTheSet()
    {
        await using var migrator = CreateMigrator(fixture);

        var applied = (await migrator.ApplyAllAsync()).ValueOrThrow();

        applied.Should().ContainSingle().Which.Applied.Should().Equal(SchemaShape.Migrations);
        foreach (var table in SchemaShape.AllTables)
            (await migrator.HasTableAsync(Schema, table)).Should().BeTrue($"the set creates {table}");
        (await HasColumnAsync(migrator, "identity_users", "deleted_at")).Should().BeTrue("001-accounts creates the soft-delete column");
        (await migrator.ReadHistoryAsync(IdentityDatabaseOptions.MigrationSetName)).Select(row => row.Ordinal).Should().Equal(1, 2, 3);
        (await migrator.ApplyAllAsync()).ValueOrThrow().Single().Applied.Should().BeEmpty("an applied set is current");
    }

    [Fact]
    public async Task ApplyRollbackApply_ShouldRebuildTheSchema()
    {
        await using var migrator = CreateMigrator(fixture);
        var runner = migrator.RunnerFor(IdentityDatabaseOptions.MigrationSetName);
        (await migrator.ApplyAllAsync()).ValueOrThrow();

        (await runner.RollbackAsync(targetOrdinal: 2)).ValueOrThrow().Should().Equal("003-spine");
        foreach (var table in SchemaShape.SpineTables)
            (await migrator.HasTableAsync(Schema, table)).Should().BeFalse($"rolling 003-spine back drops {table}");
        (await migrator.HasTableAsync(Schema, "identity_oidc_tokens")).Should().BeTrue("002-oidc stays applied");

        (await runner.RollbackAsync(targetOrdinal: 1)).ValueOrThrow().Should().Equal("002-oidc");
        foreach (var table in SchemaShape.OidcTables)
            (await migrator.HasTableAsync(Schema, table)).Should().BeFalse($"rolling 002-oidc back drops {table}");
        (await migrator.HasTableAsync(Schema, "identity_users")).Should().BeTrue("001-accounts stays applied");

        (await runner.RollbackAsync(targetOrdinal: 0)).ValueOrThrow().Should().Equal("001-accounts");
        foreach (var table in SchemaShape.AllTables)
            (await migrator.HasTableAsync(Schema, table)).Should().BeFalse($"the rollback drops {table}");
        (await migrator.ReadHistoryAsync(IdentityDatabaseOptions.MigrationSetName)).Should().BeEmpty();

        (await migrator.ApplyAllAsync()).ValueOrThrow().Single().Applied.Should().Equal(SchemaShape.Migrations);
        foreach (var table in SchemaShape.AllTables)
            (await migrator.HasTableAsync(Schema, table)).Should().BeTrue($"the second apply recreates {table}");
    }

    [Fact]
    public async Task AppliedSchema_ShouldMatchTheModel()
    {
        await using var migrator = CreateMigrator(fixture);
        (await migrator.ApplyAllAsync()).ValueOrThrow();
        await using var context = SchemaShape.CreateContext(fixture.ConnectionString, Schema);
        await using var connection = await migrator.OpenConnectionAsync();

        var model = SchemaShape.FromModel(context);
        var applied = await SchemaShape.FromDatabaseAsync(connection, Schema);

        model.Select(table => table.Name).Should().Equal(SchemaShape.AllTables);
        applied.Should().BeEquivalentTo(model, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task ModuleRegistration_ShouldMigrateAndMapTheConfiguredSchema()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDatabaseSettings(fixture.ConnectionString);
        services.AddUserAccounts(configureDatabase: database => database.Schema = "identity_tenant");
        await using var provider = services.BuildServiceProvider();

        var set = provider.GetRequiredService<IMigrationSetRegistry>().GetOptions(IdentityDatabaseOptions.MigrationSetName);
        set.Should().BeEquivalentTo(
            new { Provider = DatabaseProvider.Postgres, SchemaName = "identity_tenant", SchemaOwner = IdentitySchemaOwnerConstants.Module, TableName = "identity_migration_history" },
            options => options.ExcludingMissingMembers());
        (await provider.ApplyMigrationSetsAsync("test")).ValueOrThrow().Single().Applied.Should().Equal(SchemaShape.Migrations);

        var user = new IdentityUser { UserName = "alice", Email = "alice@example.test" };
        await using (var scope = provider.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<UserAccountService>().CreateAsync(user)).Succeeded.Should().BeTrue();

        await using var next = provider.CreateAsyncScope();
        var found = await next.ServiceProvider.GetRequiredService<UserAccountService>().FindByNameAsync("ALICE");
        found.Should().NotBeNull();
        found!.Id.Should().Be(user.Id);
        found.Id.Version.Should().Be(7);

        // The OIDC tables take OpenIddict's entities through the same context: a client and a token tied to it.
        var expires = new DateTime(2026, 10, 27, 21, 0, 0, DateTimeKind.Utc);
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var application = new IdentityOidcApplication { ClientId = "wheelhouse" };
            context.OidcApplications.Add(application);
            context.OidcTokens.Add(new IdentityOidcToken { Application = application, Subject = user.Id.ToString(), Type = "refresh_token", ExpirationDate = expires });
            await context.SaveChangesAsync();
        }

        await using var last = provider.CreateAsyncScope();
        var token = await last.ServiceProvider.GetRequiredService<IdentityDbContext>().OidcTokens.Include(t => t.Application).SingleAsync();
        token.Application!.ClientId.Should().Be("wheelhouse");
        token.ExpirationDate.Should().Be(expires);
        token.Id.Version.Should().Be(7);
    }

    [Fact]
    public async Task SpineTables_ShouldRoundTripTheirRows()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDatabaseSettings(fixture.ConnectionString);
        services.AddUserAccounts();
        await using var provider = services.BuildServiceProvider();
        (await provider.ApplyMigrationSetsAsync("test")).ValueOrThrow();

        var at = new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
        var session = new IdentitySession
        {
            Id = Guid.CreateVersion7(at),
            UserId = Guid.CreateVersion7(at),
            AuthenticationMethods = ["pwd", "mfa"],
            SecurityStamp = "stamp",
            CreatedAt = at,
            LastSeenAt = at,
            ExpiresAt = at.AddHours(12),
        };
        var ringKey = new IdentityDataProtectionKey { FriendlyName = "key-1", Xml = "<key />" };
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            context.Sessions.Add(session);
            context.SignInLog.AddRange(
                new IdentitySignInLogEntry { OccurredAt = at, Method = "password", Result = "failed", Reason = "unknown_account" },
                new IdentitySignInLogEntry { OccurredAt = at, UserId = session.UserId, Method = "password", Result = "succeeded", SessionId = session.Id });
            context.DataProtectionKeys.Add(ringKey);
            await context.SaveChangesAsync();
        }

        await using var read = provider.CreateAsyncScope();
        var stored = read.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await stored.Sessions.SingleAsync()).Should().BeEquivalentTo(session);
        (await stored.SignInLog.OrderBy(e => e.Id).Select(e => e.Id).ToListAsync()).Should().Equal(1, 2);
        (await stored.SignInLog.SingleAsync(e => e.SessionId == session.Id)).UserId.Should().Be(session.UserId);
        (await stored.DataProtectionKeys.SingleAsync()).Should().BeEquivalentTo(new { ringKey.Id, FriendlyName = "key-1", Xml = "<key />" });
    }

    /// <summary>Returns whether <paramref name="table"/> in the identity schema has <paramref name="column"/>.</summary>
    internal static async Task<bool> HasColumnAsync(MigratorHarness migrator, string table, string column)
    {
        await using var connection = await migrator.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<bool>(
            "select exists (select 1 from information_schema.columns where table_schema = @schema and table_name = @table and column_name = @column)",
            new { schema = Schema, table, column });
    }

    /// <summary>A harness running the Module's embedded SQL as the <c>identity</c> set, rollback allowed.</summary>
    internal static MigratorHarness CreateMigrator(MigratorPostgresFixture fixture) => MigratorHarness.CreatePostgres(fixture.ConnectionString, sets => sets.Add(
        IdentityDatabaseOptions.MigrationSetName,
        _ => new EmbeddedResourceMigrationBroker(typeof(IdentityDbContext).Assembly, IdentityDatabaseServiceCollectionExtensions.MigrationResourcePrefix),
        options =>
        {
            options.SchemaName = Schema;
            options.SchemaOwner = IdentitySchemaOwnerConstants.Module;
            options.AllowRollback = true;
        }));
}

/// <summary>Shares one PostgreSQL container across the migration tests.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresMigratorCollection : ICollectionFixture<MigratorPostgresFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "identity-postgres-migrator";
}

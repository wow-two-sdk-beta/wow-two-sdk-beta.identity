using AwesomeAssertions;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations.Lint;

namespace Identity.Tests.Migrations;

/// <summary>
/// The rules the <c>identity</c> set ships under: the be sdk lint over the set as the Module registers it, the tables it
/// publishes to products, and what <c>004-user-soft-delete</c> lets the database hold.
/// </summary>
/// <param name="fixture">The shared container.</param>
[Collection(PostgresMigratorCollection.Name)]
public sealed class IdentityMigrationRulesTests(MigratorPostgresFixture fixture) : IAsyncLifetime
{
    /// <summary>The last ordinal applied before the lint: <c>001</c>–<c>003</c> stay as they were applied.</summary>
    private const int LintCutoff = 3;

    /// <inheritdoc />
    public async Task InitializeAsync() => await fixture.ResetAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Lint_ShouldFindNothing_AboveTheAppliedFiles()
    {
        await using var provider = CreateModuleProvider();

        var findings = await MigrationLint.CheckAsync(provider, lint => lint
            .ExpandOnlyAfter(IdentityDatabaseOptions.MigrationSetName, LintCutoff)
            .LintAfter(IdentityDatabaseOptions.MigrationSetName, LintCutoff));

        findings.Should().BeEmpty();
    }

    [Fact]
    public async Task Lint_ShouldFindNothing_WhenTheLockRiskRulesReadEveryFile()
    {
        await using var provider = CreateModuleProvider();

        var findings = await MigrationLint.CheckAsync(provider, lint => lint.ExpandOnlyAfter(IdentityDatabaseOptions.MigrationSetName, LintCutoff));

        findings.Should().BeEmpty("001–003 only index the tables they create, and 004 builds its indexes concurrently");
    }

    [Fact]
    public async Task ReferenceableTables_ShouldPublishAccountsAndOrganizations_WithoutACredentialColumn()
    {
        await using var provider = CreateModuleProvider();
        var published = provider.GetRequiredService<IMigrationSetRegistry>().GetOptions(IdentityDatabaseOptions.MigrationSetName).ReferenceableTables;

        published.Should().BeEquivalentTo(
        [
            new MigrationReferenceableTable("identity_users", ["id"], ["id", "user_name", "email", "created_at", "updated_at", "deleted_at"]),
            new MigrationReferenceableTable("identity_organizations", ["id"], ["id", "name", "slug", "created_at"]),
        ], options => options.WithStrictOrdering());
        published.SelectMany(table => table.ReadableColumns).Should().NotContain(column =>
            column.Contains("hash") || column.Contains("stamp") || column.Contains("phone") || column.Contains("lockout") || column.StartsWith("normalized_"));

        (await provider.ApplyMigrationSetsAsync("test")).ValueOrThrow();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        foreach (var table in published)
        {
            var columns = (await connection.QueryAsync<string>(
                "select column_name from information_schema.columns where table_schema = @schema and table_name = @table",
                new { schema = IdentitySchemaTests.Schema, table = table.Table })).ToList();
            columns.Should().Contain(table.Keys.Concat(table.ReadableColumns), $"{table.Table} publishes only columns it has");
        }
    }

    [Fact]
    public async Task UserNames_ShouldStayUniqueAmongLiveAccounts_AndFreeOnceDeleted()
    {
        await using var migrator = IdentitySchemaTests.CreateMigrator(fixture);
        (await migrator.ApplyAllAsync()).ValueOrThrow();
        await using var connection = await migrator.OpenConnectionAsync();
        var at = new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

        await InsertUserAsync(connection, Guid.CreateVersion7(at), "ALICE", deletedAt: at);
        await InsertUserAsync(connection, Guid.CreateVersion7(at), "ALICE", deletedAt: at.AddMinutes(1));
        await InsertUserAsync(connection, Guid.CreateVersion7(at), "ALICE", deletedAt: null);
        var duplicate = () => InsertUserAsync(connection, Guid.CreateVersion7(at), "ALICE", deletedAt: null);

        (await duplicate.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        (await connection.ExecuteScalarAsync<long>("select count(*) from identity.identity_users where normalized_user_name = 'ALICE'")).Should().Be(3);
    }

    [Fact]
    public async Task RollingBackTheSoftDelete_ShouldDropTheDeletedRows_AndKeepTheLiveOnes()
    {
        await using var migrator = IdentitySchemaTests.CreateMigrator(fixture);
        (await migrator.ApplyAllAsync()).ValueOrThrow();
        var at = new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
        var live = Guid.CreateVersion7(at);
        await using (var connection = await migrator.OpenConnectionAsync())
        {
            await InsertUserAsync(connection, Guid.CreateVersion7(at), "BOB", deletedAt: at);
            await InsertUserAsync(connection, live, "BOB", deletedAt: null);
        }

        (await migrator.RunnerFor(IdentityDatabaseOptions.MigrationSetName).RollbackAsync(targetOrdinal: LintCutoff)).ValueOrThrow()
            .Should().Equal("004-user-soft-delete");

        await using var after = await migrator.OpenConnectionAsync();
        (await after.QueryAsync<Guid>("select id from identity.identity_users")).Should().Equal(live);
        (await IdentitySchemaTests.HasColumnAsync(migrator, "identity_users", "deleted_at")).Should().BeFalse();
    }

    /// <summary>The set exactly as the Module registers it: its options, published tables included, and its embedded SQL.</summary>
    private ServiceProvider CreateModuleProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDatabaseSettings(fixture.ConnectionString);
        services.AddUserAccounts();
        return services.BuildServiceProvider();
    }

    private static Task InsertUserAsync(System.Data.Common.DbConnection connection, Guid id, string normalizedUserName, DateTimeOffset? deletedAt) =>
        connection.ExecuteAsync(
            """
            insert into identity.identity_users (id, user_name, normalized_user_name, email_confirmed, phone_number_confirmed,
                two_factor_enabled, lockout_enabled, access_failed_count, created_at, updated_at, deleted_at)
            values (@id, @userName, @normalizedUserName, false, false, false, false, 0, @at, @at, @deletedAt)
            """,
            new { id, userName = normalizedUserName.ToLowerInvariant(), normalizedUserName, at = DateTimeOffset.UnixEpoch, deletedAt });
}

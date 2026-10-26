using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Identity.Module;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace Identity.Tests.Migrations;

/// <summary>The module's embedded migrations over a throwaway SQLite file: apply, idempotency, and the Apply → Rollback → Apply round trip.</summary>
public sealed class MigrationTests : IDisposable
{
    private static readonly string[] Names = ["baseline"];

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"identity-migrations-{Guid.NewGuid():N}.db");

    /// <summary>Gets the connection string; pooling is off so the file is unlocked for teardown.</summary>
    private string ConnectionString => $"Data Source={databasePath};Pooling=False";

    public void Dispose()
    {
        if (File.Exists(databasePath))
            File.Delete(databasePath);
    }

    private MigratorHarness CreateMigrator(Action<MigrationOptions>? configure = null) =>
        MigratorHarness.CreateSqlite(ConnectionString, typeof(IdentityModuleAssembly).Assembly, configure);

    [Fact]
    public async Task ApplyPending_ShouldCreateTheModuleSchemaAndBeIdempotent_WhenTheDatabaseIsFresh()
    {
        await using var migrator = CreateMigrator();

        var applied = Unwrap(await migrator.Runner.ApplyPendingAsync("startup", CancellationToken.None));

        applied.Should().Equal("001-baseline");
        (await migrator.HasTableAsync("identity_greeting_logs")).Should().BeTrue();
        var history = await migrator.ReadHistoryAsync();
        history.Select(row => row.Ordinal).Should().Equal(1);
        history.Select(row => row.Name).Should().Equal(Names);
        history.Should().OnlyContain(row => row.AppliedBy == "startup" && row.Checksum.Length == 64);

        Unwrap(await migrator.Runner.ApplyPendingAsync("startup", CancellationToken.None)).Should().BeEmpty();
        (await migrator.ReadHistoryAsync()).Should().HaveCount(1);
    }

    [Fact]
    public async Task ApplyPending_ShouldRestoreTheSchema_WhenEveryMigrationWasRolledBack()
    {
        await using var migrator = CreateMigrator(options => options.AllowRollback = true);
        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None));

        Unwrap(await migrator.Runner.RollbackAsync(targetOrdinal: 0, CancellationToken.None))
            .Should().Equal("001-baseline");
        (await migrator.HasTableAsync("identity_greeting_logs")).Should().BeFalse();
        (await migrator.ReadHistoryAsync()).Should().BeEmpty();

        Unwrap(await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None)).Should().Equal("001-baseline");
        (await migrator.HasTableAsync("identity_greeting_logs")).Should().BeTrue();
        var status = Unwrap(await migrator.Runner.GetStatusAsync(CancellationToken.None));
        status.Applied.Select(applied => applied.Ordinal).Should().Equal(1);
        status.Pending.Should().BeEmpty();
        status.Drifted.Should().BeEmpty();
    }

    /// <summary>Returns a migrator result's value, failing the test with the migrator's own message otherwise.</summary>
    private static T Unwrap<T>(Result<T> result) where T : notnull =>
        result is Result<T>.Success success
            ? success.Value
            : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);
}

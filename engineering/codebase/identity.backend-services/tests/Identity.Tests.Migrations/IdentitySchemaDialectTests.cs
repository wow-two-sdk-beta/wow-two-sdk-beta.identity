using Microsoft.Data.Sqlite;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace Identity.Tests.Migrations;

/// <summary>The identity set on SQLite: a throwaway file per test, no Docker.</summary>
public sealed class SqliteIdentitySchemaTests : IdentitySchemaTests
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"identity-migrations-{Guid.NewGuid():N}.db");

    /// <inheritdoc />
    protected override DatabaseProvider Provider => DatabaseProvider.Sqlite;

    /// <inheritdoc />
    protected override string ConnectionString => $"Data Source={_path}";

    /// <inheritdoc />
    public override Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public override Task DisposeAsync()
    {
        // Pooled connections hold the file open, so release them before the files go.
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _path, _path + "-shm", _path + "-wal" })
            File.Delete(path);
        return Task.CompletedTask;
    }
}

/// <summary>The identity set on PostgreSQL in a Testcontainers instance, every user schema dropped before each test.</summary>
/// <param name="fixture">The shared container.</param>
[Collection(PostgresMigratorCollection.Name)]
public sealed class PostgresIdentitySchemaTests(MigratorPostgresFixture fixture) : IdentitySchemaTests
{
    /// <inheritdoc />
    protected override DatabaseProvider Provider => DatabaseProvider.Postgres;

    /// <inheritdoc />
    protected override string ConnectionString => fixture.ConnectionString;

    /// <inheritdoc />
    public override async Task InitializeAsync() => await fixture.ResetAsync();

    /// <inheritdoc />
    public override Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>Shares one PostgreSQL container across the PostgreSQL migration tests.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresMigratorCollection : ICollectionFixture<MigratorPostgresFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "identity-postgres-migrator";
}

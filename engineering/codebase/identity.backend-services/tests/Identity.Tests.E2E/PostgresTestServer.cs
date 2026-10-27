using Npgsql;
using Testcontainers.PostgreSql;

namespace Identity.Tests.E2E;

/// <summary>
/// One PostgreSQL container for the whole tier, started on first use and removed when the test process ends; each API
/// host takes a database of its own on it, so tests stay isolated and run in parallel without a container each.
/// </summary>
public static class PostgresTestServer
{
    private static readonly Lazy<Task<PostgreSqlContainer>> Container = new(StartAsync);

    /// <summary>Creates an empty database and returns its connection string.</summary>
    public static async Task<string> CreateDatabaseAsync()
    {
        var server = await Container.Value;
        var name = $"identity_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(server.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(server.GetConnectionString()) { Database = name }.ConnectionString;
    }

    /// <summary>Drops a database <see cref="CreateDatabaseAsync"/> made, closing whatever connections still hold it.</summary>
    /// <param name="connectionString">The database's connection string.</param>
    public static async Task DropDatabaseAsync(string connectionString)
    {
        var name = new NpgsqlConnectionStringBuilder(connectionString).Database;
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection((await Container.Value).GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<PostgreSqlContainer> StartAsync()
    {
        var container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        await container.StartAsync();
        return container;
    }
}

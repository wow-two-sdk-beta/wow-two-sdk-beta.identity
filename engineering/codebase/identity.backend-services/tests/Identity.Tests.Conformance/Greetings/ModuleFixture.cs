using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Identity.Module;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace Identity.Tests.Conformance.Greetings;

/// <summary>Form 1: the module wired as a consumer wires it, over a throwaway SQLite file its own migrations build.</summary>
public sealed class ModuleFixture : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"identity-conformance-{Guid.NewGuid():N}.db");

    /// <summary>Gets the clock the module stamps greetings with.</summary>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UnixEpoch.AddDays(1));

    /// <summary>Gets the consumer's container.</summary>
    public ServiceProvider Services { get; private set; } = null!;

    async Task IAsyncLifetime.InitializeAsync()
    {
        var connectionString = $"Data Source={databasePath}";
        await using (var migrator = MigratorHarness.CreateSqlite(connectionString, typeof(IdentityModuleAssembly).Assembly))
            await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None);

        var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(Clock);
        services.AddIdentity().Configure(options => options.ConnectionString = connectionString);
        Services = services.BuildServiceProvider(validateScopes: true);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { databasePath, databasePath + "-shm", databasePath + "-wal" })
            File.Delete(path);
    }
}

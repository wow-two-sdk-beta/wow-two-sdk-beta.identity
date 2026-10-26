using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Identity.Greetings;
using WoW.Two.Sdk.Beta.Be.Identity.Module;
using WoW.Two.Sdk.Beta.Be.Identity.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace Identity.Tests.Integration.Greetings;

/// <summary>The module over a real store: every greeting it serves lands in its log table.</summary>
public sealed class ModuleGreetingsBrokerTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"identity-integration-{Guid.NewGuid():N}.db");
    private readonly FakeTimeProvider clock = new(DateTimeOffset.UnixEpoch.AddDays(1));
    private ServiceProvider services = null!;

    private string ConnectionString => $"Data Source={databasePath}";

    public async Task InitializeAsync()
    {
        await using (var migrator = MigratorHarness.CreateSqlite(ConnectionString, typeof(IdentityModuleAssembly).Assembly))
            await migrator.Runner.ApplyPendingAsync("test", CancellationToken.None);

        var collection = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(clock);
        collection.AddIdentity().Configure(options => options.ConnectionString = ConnectionString);
        services = collection.BuildServiceProvider(validateScopes: true);
    }

    public async Task DisposeAsync()
    {
        await services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { databasePath, databasePath + "-shm", databasePath + "-wal" })
            File.Delete(path);
    }

    [Fact]
    public async Task GreetAsync_ShouldLogTheGreeting_WhenItIsServed()
    {
        await using (var scope = services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IGreetingsBroker>().GreetAsync("Identity");

        await using var read = services.CreateAsyncScope();
        var logs = await read.ServiceProvider.GetRequiredService<IdentityDbContext>().GreetingLogs.ToListAsync();
        logs.Should().ContainSingle().Which.Should().Match<GreetingLog>(log =>
            log.Message == "Hello, Identity!" && log.GreetedAtUtc == clock.GetUtcNow());
    }

    [Fact]
    public async Task GreetAsync_ShouldLogNothing_WhenTheNameIsRejected()
    {
        await using (var scope = services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IGreetingsBroker>().GreetAsync(new string('a', GreetingConstants.MaxNameLength + 1));

        await using var read = services.CreateAsyncScope();
        (await read.ServiceProvider.GetRequiredService<IdentityDbContext>().GreetingLogs.CountAsync()).Should().Be(0);
    }
}

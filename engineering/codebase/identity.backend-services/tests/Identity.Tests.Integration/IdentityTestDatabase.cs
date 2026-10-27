using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Identity.Core;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests;

/// <summary>
/// One test host's private in-memory SQLite database, schema built by the Module's own migration set as a Form 1 host
/// builds it — never by <c>EnsureCreated</c>. A held-open connection keeps the shared-cache database alive while the
/// context and the migrator open and close their own.
/// </summary>
internal sealed class IdentityTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _keepAlive;

    public IdentityTestDatabase()
    {
        ConnectionString = $"Data Source=identity-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();
    }

    /// <summary>The shared-cache connection string every connection to this database uses.</summary>
    public string ConnectionString { get; }

    /// <summary>Points the Module at this database: the host's database settings, on the SQLite engine.</summary>
    public void Register(IServiceCollection services) => services.AddDatabaseSettings(ConnectionString);

    /// <summary>Selects the SQLite engine for <c>AddUserAccounts(configureDatabase: …)</c>.</summary>
    public static void UseSqlite(IdentityDatabaseOptions database) => database.Provider = DatabaseProvider.Sqlite;

    /// <summary>Applies every registered migration set, the Module's <c>identity</c> set included.</summary>
    public static async Task MigrateAsync(IServiceProvider services)
        => (await services.ApplyMigrationSetsAsync("test")).ValueOrThrow();

    public ValueTask DisposeAsync() => _keepAlive.DisposeAsync();
}

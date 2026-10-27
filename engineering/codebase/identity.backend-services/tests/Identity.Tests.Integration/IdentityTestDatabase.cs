using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests;

/// <summary>
/// One test host's private PostgreSQL database on the tier's shared container (<see cref="PostgresTestServer"/>), its
/// schema built by the Module's own migration set as a Form 1 host builds it — never by <c>EnsureCreated</c>.
/// </summary>
internal sealed class IdentityTestDatabase : IAsyncDisposable
{
    public IdentityTestDatabase()
        => ConnectionString = Task.Run(PostgresTestServer.CreateDatabaseAsync).GetAwaiter().GetResult();

    /// <summary>The database's connection string.</summary>
    public string ConnectionString { get; }

    /// <summary>Points the Module at this database through the host's database settings.</summary>
    public void Register(IServiceCollection services) => services.AddDatabaseSettings(ConnectionString);

    /// <summary>Applies every registered migration set, the Module's <c>identity</c> set included.</summary>
    public static async Task MigrateAsync(IServiceProvider services)
        => (await services.ApplyMigrationSetsAsync("test")).ValueOrThrow();

    public async ValueTask DisposeAsync() => await PostgresTestServer.DropDatabaseAsync(ConnectionString);
}

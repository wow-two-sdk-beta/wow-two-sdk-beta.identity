using WoW.Two.Sdk.Beta.Be.Data.Abstractions;
using WoW.Two.Sdk.Beta.Be.Data.Dapper;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Identity.Module;
using WoW.Two.Sdk.Beta.Be.Web.Json;

namespace Identity.Api.Configurations;

public static partial class HostConfiguration
{
    /// <summary>Hosts the Form 1 module over the service's SQLite store, with the migrator over the module's SQL.</summary>
    private static WebApplicationBuilder AddIdentityModule(this WebApplicationBuilder builder)
    {
        // Resolved lazily, so a test host's configuration overrides still reach the connection string.
        builder.Services.AddSingleton(provider => new DatabaseSettings
        {
            ConnectionString = ResolveConnectionString(provider.GetRequiredService<IConfiguration>()),
        });
        builder.Services.AddSingleton<IDbConnectionFactory>(provider =>
            new SqliteConnectionFactory(provider.GetRequiredService<DatabaseSettings>().ConnectionString));
        builder.Services.AddIdentity()
            .Configure<DatabaseSettings>((options, database) => options.ConnectionString = database.ConnectionString);
        builder.Services.AddDatabaseBespokeMigrations(typeof(IdentityModuleAssembly).Assembly, migrations =>
        {
            migrations.Provider = DatabaseProvider.Sqlite;
            migrations.Version = MigrationVersionMapper.ToVersion(typeof(HostConfiguration).Assembly);
        });
        return builder;
    }

    /// <summary>Registers controllers on the SDK wire contract.</summary>
    private static WebApplicationBuilder AddControllers(this WebApplicationBuilder builder)
    {
        builder.Services.AddControllersWithSdkJson();
        return builder;
    }

    /// <summary>Resolves the connection string: the <c>DB_CONNECTION</c> alias wins over <c>DatabaseSettings:ConnectionString</c>.</summary>
    private static string ResolveConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration["DB_CONNECTION"];
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = configuration["DatabaseSettings:ConnectionString"];

        return string.IsNullOrWhiteSpace(connectionString)
            ? throw new InvalidOperationException("Database connection string not found. Set DB_CONNECTION or DatabaseSettings:ConnectionString.")
            : connectionString;
    }
}

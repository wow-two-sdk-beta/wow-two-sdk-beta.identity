using Identity.Api.Accounts;
using Microsoft.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Sqlite;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Observability.HealthChecks;
using WoW.Two.Sdk.Beta.Be.Web.Json;

namespace Identity.Api.Configurations;

public static partial class HostConfiguration
{
    /// <summary>Hosts the Form 1 module over the service's SQLite store: user accounts on the EF store, and the store's readiness check.</summary>
    private static WebApplicationBuilder AddIdentityModule(this WebApplicationBuilder builder)
    {
        // Resolved lazily, so a test host's configuration overrides still reach the connection string.
        builder.Services.AddDbContext<IdentityAccountsDbContext>((provider, options) =>
            options.UseSqliteConventional(ResolveConnectionString(provider.GetRequiredService<IConfiguration>())));
        builder.Services.AddUserAccounts<IdentityUser>()
            .AddEntityFrameworkStores<IdentityAccountsDbContext>();
        builder.Services.AddHealthChecks().AddDatabaseReadinessCheck<IdentityAccountsDbContext>();
        return builder;
    }

    /// <summary>Registers controllers on the SDK wire contract.</summary>
    private static WebApplicationBuilder AddControllers(this WebApplicationBuilder builder)
    {
        builder.Services.AddControllersWithSdkJson();
        return builder;
    }

    /// <summary>Creates the database and the identity schema when they are missing.</summary>
    /// <remarks>The schema is EF-owned (<c>ApplyIdentitySchema</c>) until the module ships SQL migrations (T10); then the
    /// service applies those on boot instead.</remarks>
    private static async Task EnsureIdentitySchemaAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityAccountsDbContext>().Database
            .EnsureCreatedAsync(app.Lifetime.ApplicationStopping);
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

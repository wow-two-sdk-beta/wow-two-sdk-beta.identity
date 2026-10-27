using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;
using WoW.Two.Sdk.Beta.Be.Data.Dapper;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Sqlite;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>Registers the Module's own database: <see cref="IdentityDbContext"/>, the EF user store and the <c>identity</c> migration set.</summary>
internal static partial class IdentityDatabaseServiceCollectionExtensions
{
    /// <summary>
    /// Register the identity database from <see cref="IdentityDatabaseOptions"/>: the context over the configured engine, a
    /// connection factory keyed by the set name, and the embedded SQL of that engine as the be sdk migration set
    /// <see cref="IdentityDatabaseOptions.MigrationSetName"/>, claiming its schema as <see cref="IdentityDatabaseOptions.SchemaOwner"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional database options; the host section <c>Identity:Database</c> is applied last.</param>
    internal static IServiceCollection AddIdentityDatabase(this IServiceCollection services, Action<IdentityDatabaseOptions>? configure)
    {
        services.AddModuleOptions(
            IdentityDatabaseOptions.SectionName,
            configure,
            options => options
                .Validate(o => Enum.IsDefined(o.Provider), "IdentityDatabaseOptions.Provider must be Postgres or Sqlite.")
                .Validate(o => o.Schema is not null && SchemaNamePattern().IsMatch(o.Schema), "IdentityDatabaseOptions.Schema must be a lowercase identifier (a-z first, then a-z, 0-9 and _, 63 characters at most).")
                .Validate(o => o.ConnectionStringName is null || !string.IsNullOrWhiteSpace(o.ConnectionStringName), "IdentityDatabaseOptions.ConnectionStringName must be null or non-empty.")
                .Validate(
                    o => o.SchemaOwner is IdentitySchemaOwnerConstants.Module or IdentitySchemaOwnerConstants.Service,
                    $"IdentityDatabaseOptions.SchemaOwner must be '{IdentitySchemaOwnerConstants.Module}' or '{IdentitySchemaOwnerConstants.Service}'."));
        services.TryAddSingleton(IdentityDatabaseConnection.Resolve);

        // The set's own database, so the be sdk's boot and deploy helpers create it when missing.
        services.AddKeyedSingleton(IdentityDatabaseOptions.MigrationSetName, (provider, _) =>
            new DatabaseSettings { ConnectionString = provider.GetRequiredService<IdentityDatabaseConnection>().ConnectionString });

        // One pool for the context and the migration set; built only on PostgreSQL, disposed with the container.
        services.AddKeyedSingleton(IdentityDatabaseOptions.MigrationSetName, (provider, _) =>
            NpgsqlDataSource.Create(provider.GetRequiredService<IdentityDatabaseConnection>().ConnectionString));
        services.AddKeyedSingleton<IDbConnectionFactory>(IdentityDatabaseOptions.MigrationSetName, (provider, _) =>
            provider.GetRequiredService<IdentityDatabaseConnection>() is { Provider: DatabaseProvider.Sqlite } sqlite
                ? new SqliteConnectionFactory(sqlite.ConnectionString)
                : new DataSourceConnectionFactory(provider.GetRequiredKeyedService<NpgsqlDataSource>(IdentityDatabaseOptions.MigrationSetName)));

        // The SDK context path, so the host's registered interceptors (audit stamps first) attach as on its own context.
        services.AddEntityFrameworkCore<IdentityDbContext>(
            options => options.UsePooling = false,
            static (provider, builder) =>
            {
                if (provider.GetRequiredService<IdentityDatabaseConnection>() is { Provider: DatabaseProvider.Sqlite } sqlite)
                    builder.UseSqliteConventional(sqlite.ConnectionString);
                else
                    builder.UseNpgsql(provider.GetRequiredKeyedService<NpgsqlDataSource>(IdentityDatabaseOptions.MigrationSetName));
            });

        // The schema is the Module's: its embedded SQL for the configured engine, under its own history, lock and owner marker.
        services.AddDatabaseBespokeMigrationSet(
            IdentityDatabaseOptions.MigrationSetName,
            static provider => new EmbeddedResourceMigrationBroker(
                typeof(IdentityDbContext).Assembly,
                MigrationResourcePrefix(provider.GetRequiredService<IdentityDatabaseOptions>().Provider)));
        services.AddOptions<MigrationOptions>(IdentityDatabaseOptions.MigrationSetName)
            .Configure<IdentityDatabaseOptions>(static (migration, database) =>
            {
                migration.Provider = database.Provider;
                migration.SchemaName = database.Schema;
                migration.SchemaOwner = database.SchemaOwner;
            });

        services.TryAddScoped<IUserRepository, EfUserRepository>();
        return services;
    }

    /// <summary>The embedded-resource folder holding one engine's migrations: <c>Migrations/Postgres/</c> or <c>Migrations/Sqlite/</c>.</summary>
    /// <param name="provider">The engine.</param>
    internal static string MigrationResourcePrefix(DatabaseProvider provider) => $"Migrations/{provider}/";

    [GeneratedRegex("^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex SchemaNamePattern();
}

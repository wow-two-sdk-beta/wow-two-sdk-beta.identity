using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>The identity database the context and the migration set both reach: the engine and its connection string.</summary>
/// <param name="Provider">The engine.</param>
/// <param name="ConnectionString">The connection string.</param>
internal sealed record IdentityDatabaseConnection(DatabaseProvider Provider, string ConnectionString)
{
    /// <summary>The variable that overrides the host's configured connection string, as in the be sdk persistence bundle.</summary>
    private const string ConnectionStringVariable = "DB_CONNECTION";

    /// <summary>The host's configured connection string key, as in the be sdk persistence bundle.</summary>
    private const string ConnectionStringKey = "DatabaseSettings:ConnectionString";

    /// <summary>Resolves the connection at first use, so a test host's configuration overrides still reach it.</summary>
    /// <param name="services">The root provider.</param>
    /// <exception cref="InvalidOperationException">No source yields a connection string.</exception>
    internal static IdentityDatabaseConnection Resolve(IServiceProvider services)
    {
        var options = services.GetRequiredService<IdentityDatabaseOptions>();
        var configuration = services.GetService<IConfiguration>();
        return new IdentityDatabaseConnection(options.Provider, options.ConnectionStringName is { } name
            ? NonBlank(configuration?.GetConnectionString(name))
                ?? throw new InvalidOperationException($"Identity database connection string 'ConnectionStrings:{name}' not found ({IdentityDatabaseOptions.SectionName}:ConnectionStringName).")
            : NonBlank(services.GetService<DatabaseSettings>()?.ConnectionString)
                ?? NonBlank(configuration?[ConnectionStringVariable])
                ?? NonBlank(configuration?[ConnectionStringKey])
                ?? throw new InvalidOperationException(
                    $"Identity database connection string not found. Set {ConnectionStringVariable} or {ConnectionStringKey}, or name one with {IdentityDatabaseOptions.SectionName}:ConnectionStringName."));
    }

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

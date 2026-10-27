using System.Data.Common;
using WoW.Two.Sdk.Beta.Be.Data;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Meta;

namespace Identity.Api.Configurations;

/// <summary>Extends the host builder and the web application for startup wiring.</summary>
public static partial class HostConfiguration
{
    /// <summary>Configures the application builder (services).</summary>
    /// <param name="builder">The web application builder to configure.</param>
    public static WebApplicationBuilder Configure(this WebApplicationBuilder builder)
    {
        // Lay the SDK boot floor before any domain seam. It reads the settings file a target mounts
        // (appsettings.Target.json) and, on a local run, this machine's (appsettings.Local.json). The hosted pages and
        // the console are served from the same host.
        builder.AddApiDefaults(options =>
        {
            options.ServiceName = "identity";
            options.EnableOtlpExporters = false;
            options.EnableHttpsRedirection = !builder.Environment.IsDevelopment();
            options.SpaHosting = _ => { };
            options.TrustProxiesFrom(builder.Configuration.GetSection("Deployment"));
        });

        return builder
            .AddIdentityModule()
            .AddControllers();
    }

    /// <summary>Runs startup tasks, then configures middleware and endpoints.</summary>
    /// <param name="app">The built web application to configure.</param>
    public static async Task<WebApplication> ConfigureAsync(this WebApplication app)
    {
        // Apply the module's identity schema on a local run; a deployed boot only verifies the deploy's migrate step ran.
        await app.MigrateIdentitySchemaAsync();

        // SDK pipeline: secure headers, compression, the SPA bundle, /health (database readiness) and the SPA fallback.
        app.UseApiDefaults();
        app.MapControllers();

        return app;
    }

    /// <summary>
    /// The deploy's migrate step (<c>dotnet Identity.Api.dll migrate</c>): creates the database if missing, applies the
    /// identity migration set and claims its schema as this instance, then returns the process exit code.
    /// </summary>
    /// <param name="app">The built web application; it is never started.</param>
    /// <returns><c>0</c> applied or already current · <c>1</c> refused (drift, orphaned history, another owner) · <c>2</c> a script failed.</returns>
    public static async Task<int> MigrateAsync(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Identity.Api.Migrate");
        try
        {
            var result = await app.Services.MigrateBespokeForDeployAsync(cancellationToken: app.Lifetime.ApplicationStopping);
            if (result.IsFailure(out var error, out var applied))
            {
                logger.LogError("Migrate refused: {Error}", error.Message);
                return 1;
            }

            foreach (var set in applied)
                logger.LogInformation("Migration set {Set} applied {Count} migration(s): {Applied}", set.Set, set.Applied.Count, string.Join(", ", set.Applied));
            return 0;
        }
        catch (DbException exception)
        {
            logger.LogError(exception, "Migrate failed; the failed batch rolled back.");
            return 2;
        }
    }
}

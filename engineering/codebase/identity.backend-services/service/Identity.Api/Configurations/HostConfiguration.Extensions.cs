using WoW.Two.Sdk.Beta.Be.Data;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Audit;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Observability.HealthChecks;
using WoW.Two.Sdk.Beta.Be.Web.Json;

namespace Identity.Api.Configurations;

public static partial class HostConfiguration
{
    /// <summary>
    /// Hosts the Form 1 module over the service's own database, claiming the identity schema as the service: user
    /// accounts on the module's context, its migration set, audit stamps and the store's readiness check.
    /// </summary>
    /// <remarks>The engine comes from <c>Identity:Database:Provider</c>; the connection is the host's database.</remarks>
    private static WebApplicationBuilder AddIdentityModule(this WebApplicationBuilder builder)
    {
        builder.Services.AddEfCoreAuditInterceptor();
        builder.Services.AddUserAccounts(configureDatabase: database => database.SchemaOwner = IdentitySchemaOwnerConstants.Service);
        builder.Services.AddHealthChecks().AddDatabaseReadinessCheck<IdentityDbContext>();
        return builder;
    }

    /// <summary>Registers controllers on the SDK wire contract.</summary>
    private static WebApplicationBuilder AddControllers(this WebApplicationBuilder builder)
    {
        builder.Services.AddControllersWithSdkJson();
        return builder;
    }

    /// <summary>Applies the identity schema on a local run, or verifies its level read-only on a deployed boot.</summary>
    /// <remarks>A deployed boot fails while the schema is behind: the deploy's <c>migrate</c> step applies it first.</remarks>
    private static async Task MigrateIdentitySchemaAsync(this WebApplication app)
        => await app.Services.MigrateBespokeOnStartupAsync(app.Lifetime.ApplicationStopping);
}

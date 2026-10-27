using WoW.Two.Sdk.Beta.Be.Data;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Audit;
using WoW.Two.Sdk.Beta.Be.Identity.Cookies;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Lockout;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SecurityStamps;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;
using WoW.Two.Sdk.Beta.Be.Identity.DataProtection;
using WoW.Two.Sdk.Beta.Be.Identity.OidcServer;
using WoW.Two.Sdk.Beta.Be.Observability.HealthChecks;
using WoW.Two.Sdk.Beta.Be.Web.Json;

namespace Identity.Api.Configurations;

public static partial class HostConfiguration
{
    /// <summary>The account API's mount point, under the contract's HTTP mirror.</summary>
    internal const string AccountPath = "/api/account";

    /// <summary>
    /// Hosts the Form 1 module over the service's own PostgreSQL database, claiming the identity schema as the service:
    /// user accounts on the module's context with password sign-in, lockout, stamps, organizations, sessions and the
    /// sign-in log, its migration set, audit stamps and the store's readiness check; the OIDC server over the same
    /// store; and the Data Protection ring in that database.
    /// </summary>
    /// <remarks>
    ///   - the connection is the host's database (<c>DatabaseSettings:ConnectionString</c> or <c>DB_CONNECTION</c>)
    ///   - the server's clients, lifetimes and keys come from <c>Identity:Server</c>; a deployed boot refuses ephemeral keys
    ///   - the ring's wrapping certificates come from <c>Identity:DataProtection</c>; a deployed boot refuses none
    /// </remarks>
    private static WebApplicationBuilder AddIdentityModule(this WebApplicationBuilder builder)
    {
        builder.Services.AddEfCoreAuditInterceptor();
        builder.Services.AddUserAccounts(configureDatabase: database => database.SchemaOwner = IdentitySchemaOwnerConstants.Service)
            .AddArgon2Passwords()
            .AddSignIn()
            .AddLockout()
            .AddSecurityStampValidation()
            .AddSessions()
            .AddSignInLog()
            .AddOrganizations()
            .AddAccountEndpoints()
            .AddOidcServer()
            .AddDataProtectionRing();
        builder.Services.AddHealthChecks().AddDatabaseReadinessCheck<IdentityDbContext>();
        return builder;
    }

    /// <summary>
    /// The sign-in session the authorization endpoint reads: the account API's cookie (12 h absolute, as the session
    /// row, raw 401 for API callers), sealed by the database ring <c>AddDataProtectionRing</c> registers.
    /// </summary>
    private static WebApplicationBuilder AddSignInSession(this WebApplicationBuilder builder)
    {
        builder.Services.AddCookieAuthentication(options =>
        {
            options.CookieName = "__Host-identity";
            options.ExpireTimeSpan = TimeSpan.FromHours(12);
            options.SlidingExpiration = false;
            options.Mode = AuthChallengeMode.Api;
        });
        builder.Services.AddAuthorization();
        return builder;
    }

    /// <summary>Maps the account API (<see cref="AccountPath"/>) and the OIDC server's pass-through endpoints.</summary>
    private static WebApplication MapIdentityEndpoints(this WebApplication app)
    {
        app.MapGroup(AccountPath).MapUserAccountEndpoints();
        app.MapOidcServerEndpoints();
        return app;
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

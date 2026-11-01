using WoW.Two.Sdk.Beta.Be.Data;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Audit;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;
using WoW.Two.Sdk.Beta.Be.Identity.Cookies;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Emails;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Lockout;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SecurityStamps;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.DataProtection;
using WoW.Two.Sdk.Beta.Be.Identity.OidcServer;
using WoW.Two.Sdk.Beta.Be.Identity.Otp.Email;
using WoW.Two.Sdk.Beta.Be.Observability.HealthChecks;
using WoW.Two.Sdk.Beta.Be.Web.Json;

namespace Identity.Api.Configurations;

public static partial class HostConfiguration
{
    /// <summary>The account API's mount point, under the contract's HTTP mirror.</summary>
    internal const string AccountPath = "/api/account";

    /// <summary>The host section of the purpose-token key: <c>SigningKey</c> (base64, at least 32 bytes, or the path of a file holding it) and <c>IsKeyEphemeral</c>.</summary>
    internal const string TokensSection = "Identity:Tokens";

    /// <summary>
    /// Hosts the Form 1 module over the service's own PostgreSQL database, claiming the identity schema as the service:
    /// user accounts on the module's context with password sign-in, lockout, stamps, organizations, sessions and the
    /// sign-in log, its migration set, audit stamps and the store's readiness check; the OIDC server over the same
    /// store; and the Data Protection ring in that database.
    /// </summary>
    /// <remarks>
    ///   - the connection is the host's database (<c>DatabaseSettings:ConnectionString</c> or <c>DB_CONNECTION</c>)
    ///   - the password, sign-in and lockout rules come from <c>Identity:Password</c>, <c>Identity:SignIn</c> and
    ///     <c>Identity:Lockout</c>; the change confirmation from <c>Identity:Password:Change</c>
    ///   - the server's clients, lifetimes and keys come from <c>Identity:Server</c>; a deployed boot refuses ephemeral keys
    ///   - the ring's wrapping certificates come from <c>Identity:DataProtection</c>; a deployed boot refuses none
    ///   - confirmation, reset and change emails go over the SMTP relay of <c>Identity:Email</c>; with none configured the
    ///     instance boots and skips and logs every send
    ///   - the links in those emails point at the hosted pages on the origin of <c>Identity:Server:Issuer</c>
    ///   - the purpose tokens (confirmation, reset, forced-change ticket) are signed with <c>Identity:Tokens:SigningKey</c>
    ///   - the hosted pages' brand, theme and background come from <c>Identity:Pages</c>, each client's own look from
    ///     <c>Identity:Server:Clients:N:Theme</c> and <c>BackgroundUrl</c>; the sign-in declaration answers them
    /// </remarks>
    private static WebApplicationBuilder AddIdentityModule(this WebApplicationBuilder builder)
    {
        builder.Services.AddEfCoreAuditInterceptor();
        builder.Services.AddUserAccounts(configureDatabase: database => database.SchemaOwner = IdentitySchemaOwnerConstants.Service)
            .AddArgon2Passwords()
            .AddUserTokens(_ => { })
            .AddEmailConfirmation()
            .AddSmtpEmail(builder.Configuration)
            .AddPasswordChange()
            .AddSignIn()
            .AddLockout()
            .AddSecurityStampValidation()
            .AddSessions()
            .AddSignInLog()
            .AddOrganizations()
            .AddAccountEndpoints()
            .AddSignInDeclaration()
            .AddOidcServer()
            .AddOidcClientPageLooks()
            .AddDataProtectionRing();
        builder.Services.AddEmailOtpDelivery();
        builder.Services.AddTokenSigningKey();
        builder.Services.AddAccountPageLinks();
        builder.Services.AddHealthChecks().AddDatabaseReadinessCheck<IdentityDbContext>();
        return builder;
    }

    /// <summary>
    /// Fills the purpose tokens' key from <c>Identity:Tokens</c>: <c>SigningKey</c> is base64 of at least 32 bytes, or the path
    /// of a file holding it; a local run may set <c>IsKeyEphemeral</c> for a throwaway key, a deployed boot never.
    /// </summary>
    private static IServiceCollection AddTokenSigningKey(this IServiceCollection services)
    {
        services.AddOptions<UserTokenOptions>().PostConfigure<IHostEnvironment, IConfiguration>((options, environment, configuration) =>
        {
            var section = configuration.GetSection(TokensSection);
            section.Bind(options);
            if (!string.IsNullOrWhiteSpace(options.SigningKey))
            {
                options.SigningKey = ReadKey(options.SigningKey.Trim(), environment.ContentRootPath);
                return;
            }

            if (!section.GetValue<bool>("IsKeyEphemeral"))
                throw new InvalidOperationException($"{TokensSection}:SigningKey is required: base64 of at least 32 bytes, or the path of a file holding it.");
            if (!environment.IsLocalRun(configuration))
                throw new InvalidOperationException($"{TokensSection}:IsKeyEphemeral is allowed only on a local run (Development without DEPLOY_ENVIRONMENT); a deployed boot reads {TokensSection}:SigningKey.");

            options.SigningKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        });
        return services;
    }

    /// <summary>A key setting is the key itself or the path of a file holding it, relative to the content root.</summary>
    private static string ReadKey(string setting, string contentRoot)
    {
        var path = Path.IsPathRooted(setting) ? setting : Path.Combine(contentRoot, setting);
        return File.Exists(path) ? File.ReadAllText(path).Trim() : setting;
    }

    /// <summary>
    /// Points the links the account emails carry at the hosted pages on the instance's own origin (that of
    /// <c>Identity:Server:Issuer</c>), unless a setting already names them: <c>/confirm-email</c> and <c>/reset-password</c>,
    /// and the change page <c>/change-password#token=…</c>. With no issuer there is no origin, and no link.
    /// </summary>
    private static IServiceCollection AddAccountPageLinks(this IServiceCollection services)
    {
        services.AddOptions<UserAccountEmailSettings>().PostConfigure<OidcServerOptions>((settings, server) =>
        {
            if (OriginOf(server) is not { } origin)
                return;

            settings.ConfirmationLink ??= origin + "/confirm-email?userId={userId}&token={token}";
            settings.ResetLink ??= origin + "/reset-password?email={email}&token={token}";
        });
        services.AddOptions<IdentityCoreOptions>().PostConfigure<OidcServerOptions>((options, server) =>
        {
            if (OriginOf(server) is { } origin)
                options.Password.Change.LinkTemplate ??= origin + "/change-password#token={token}";
        });
        return services;
    }

    private static string? OriginOf(OidcServerOptions server) => server.Issuer?.GetLeftPart(UriPartial.Authority);

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
            options.IsExpirationSliding = false;
            options.Mode = AuthChallengeMode.Api;
        });
        builder.Services.AddAuthorization();
        return builder;
    }

    /// <summary>Maps the account API (<see cref="AccountPath"/>) with its sign-in declaration, and the OIDC server's pass-through endpoints.</summary>
    private static WebApplication MapIdentityEndpoints(this WebApplication app)
    {
        var account = app.MapGroup(AccountPath);
        account.MapUserAccountEndpoints();
        account.MapSignInOptionsEndpoint();
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

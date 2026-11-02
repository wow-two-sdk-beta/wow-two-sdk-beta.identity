using Identity.Api.Console.Authorization;
using Identity.Api.Settings;
using Microsoft.AspNetCore.Authorization;
using WoW.Two.Sdk.Beta.Be.Data;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Audit;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration.ConfigCheck;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Cookies;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Emails;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Lockout;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;
using WoW.Two.Sdk.Beta.Be.Identity.Core.ProfilePictures;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;
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

    /// <summary>The organization management API's mount point: members and invitations, for each organization's owners and admins.</summary>
    internal const string OrganizationsPath = "/api/organizations";

    /// <summary>
    /// The console's narrower policy for instance-wide changes, such as creating an organization: a signed-in person who
    /// holds the instance role <c>admin</c> in the store now, read on every request.
    /// </summary>
    internal const string ConsoleAdminPolicy = "identity-console-admin";

    /// <summary>The service's root settings section: <c>config check</c> refuses every key under it that no settings record binds.</summary>
    internal const string RootSection = "Identity";

    /// <summary>The host section of the purpose-token key: <c>SigningKey</c> (base64 or base64url, at least 32 bytes, or the path of a file holding it) and <c>IsKeyEphemeral</c>.</summary>
    internal const string TokensSection = TokenSettings.SectionName;

    /// <summary>
    /// Hosts the Form 1 module over the service's own PostgreSQL database, claiming the identity schema as the service:
    /// user accounts on the module's context with password sign-in, lockout, stamps, organizations, sessions and the
    /// sign-in log, its migration set, audit stamps and the store's readiness check; the OIDC server over the same
    /// store; and the Data Protection ring in that database.
    /// </summary>
    /// <remarks>
    ///   - every key under <c>Identity</c> must bind to a settings record, or <c>config check</c> and a deployed boot refuse it
    ///   - the connection is the host's database (<c>DatabaseSettings:ConnectionString</c> or <c>DB_CONNECTION</c>)
    ///   - the password, sign-in and lockout rules come from <c>Identity:Password</c>, <c>Identity:SignIn</c> and
    ///     <c>Identity:Lockout</c>; the change confirmation from <c>Identity:Password:Change</c>
    ///   - the server's clients, lifetimes and keys come from <c>Identity:Server</c>; a deployed boot refuses ephemeral keys
    ///   - the ring's wrapping certificates come from <c>Identity:DataProtection</c>; a deployed boot refuses none
    ///   - confirmation, reset and change emails go over the SMTP relay of <c>Identity:Email</c>; with none configured the
    ///     instance boots and skips and logs every send
    ///   - the links in those emails point at the hosted pages on the origin of <c>Identity:Server:Issuer</c>
    ///   - the purpose tokens (confirmation, reset, forced-change ticket) are signed with <c>Identity:Tokens:SigningKey</c>
    ///   - the hosted pages' one look, brand, theme and background, comes from <c>Identity:Pages</c>; the sign-in declaration
    ///     answers it for every app
    ///   - who signs up comes from <c>Identity:SignUp:Mode</c> (<c>Invite</c> by default); invitation links last
    ///     <c>Identity:Invitations:Lifetime</c> and lead to the hosted sign-up page
    ///   - password sign-in and email come from <c>Identity:Methods:Password</c> and <c>Identity:Methods:Email</c>, both on by
    ///     default; a phone-only instance turns both off
    ///   - the instance roles (<c>admin</c>, <c>support</c>, <c>auditor</c>) gate the console and never enter an app's tokens;
    ///     the first <c>admin</c> comes from <c>admin create</c>, and creates the organizations in the console
    /// </remarks>
    private static WebApplicationBuilder AddIdentityModule(this WebApplicationBuilder builder)
    {
        builder.Services.AddSettingsRoot(RootSection);
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
            .AddInvitations()
            .AddRoles()
            .AddAccountEndpoints()
            .AddSignInDeclaration()
            .AddOidcServer()
            .AddDataProtectionRing();
        builder.Services.AddEmailOtpDelivery();
        builder.AddTokenSigningKey();
        builder.Services.AddAccountPageLinks();
        builder.Services.AddHealthChecks().AddDatabaseReadinessCheck<IdentityDbContext>();
        return builder;
    }

    /// <summary>
    /// Registers the purpose tokens' settings from <c>Identity:Tokens</c> and fills the tokens' options from them:
    /// <c>SigningKey</c> is base64 or base64url of at least 32 bytes, or the path of a file holding it; a local run may set
    /// <c>IsKeyEphemeral</c> for a throwaway key, a deployed boot never; <c>DefaultLifetime</c> and <c>Lifetimes</c> set the lifetimes.
    /// </summary>
    private static WebApplicationBuilder AddTokenSigningKey(this WebApplicationBuilder builder)
    {
        builder.Services.AddValidatedSettings<TokenSettings>(builder.Configuration, TokenSettings.SectionName, rules => rules
            .ValidateSetting(nameof(TokenSettings.SigningKey), "required",
                settings => settings.HasKey || settings.IsKeyEphemeral,
                $"{TokensSection}:SigningKey is required: base64 or base64url of at least 32 bytes, or the path of a file holding it.")
            .ValidateSetting(nameof(TokenSettings.IsKeyEphemeral), "local-only",
                settings => settings.HasKey || !settings.IsKeyEphemeral || builder.Environment.IsLocalRun(builder.Configuration),
                $"{TokensSection}:IsKeyEphemeral is allowed only on a local run (Development without DEPLOY_ENVIRONMENT); a deployed boot reads {TokensSection}:SigningKey."));
        builder.Services.AddOptions<UserTokenOptions>().PostConfigure<IHostEnvironment, IConfiguration>((options, environment, configuration) =>
        {
            // The settings record validates the section; the options take it unchecked, so a finding names the setting, not the options type.
            configuration.GetSection(TokensSection).Bind(options);
            if (!string.IsNullOrWhiteSpace(options.SigningKey))
                options.SigningKey = ReadKey(options.SigningKey.Trim(), environment.ContentRootPath);
            else if (configuration.GetSection(TokensSection).GetValue<bool>(nameof(TokenSettings.IsKeyEphemeral)) && environment.IsLocalRun(configuration))
                options.SigningKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        });
        return builder;
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
    /// the change page <c>/change-password#token=…</c>, the invitation link <c>/sign-up?invitation=…</c> and the
    /// <c>picture</c> claim's <c>/api/account/pictures/{userId}?v={version}</c>. With no issuer there is no origin, and no
    /// link.
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
        services.AddOptions<InvitationOptions>().PostConfigure<OidcServerOptions>((options, server) =>
        {
            if (OriginOf(server) is { } origin)
                options.LinkTemplate ??= origin + "/sign-up?invitation={token}";
        });
        services.AddOptions<ProfilePictureOptions>().PostConfigure<OidcServerOptions>((options, server) =>
        {
            if (OriginOf(server) is { } origin)
                options.LinkTemplate ??= origin + AccountPath + "/pictures/{userId}?v={version}";
        });
        return services;
    }

    private static string? OriginOf(OidcServerOptions server) => server.Issuer?.GetLeftPart(UriPartial.Authority);

    /// <summary>
    /// The sign-in session the authorization endpoint reads: the account API's cookie, ending with the session row
    /// (<c>Identity:Sessions</c>; reissued as a sliding session moves) and answering a raw 401 to API callers, sealed by the
    /// database ring <c>AddDataProtectionRing</c> registers; and <see cref="ConsoleAdminPolicy"/> for the console's
    /// instance-wide changes: the instance role <c>admin</c>, which <see cref="InstanceRoleAuthorizationHandler"/> reads from
    /// the store on every request, so a grant or revoke counts from the next request rather than the next sign-in. The
    /// console's session opens to anyone holding an instance role or owning or administering an organization.
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
        builder.Services.AddScoped<IAuthorizationHandler, InstanceRoleAuthorizationHandler>();
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(ConsoleAdminPolicy, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new InstanceRoleRequirement(InstanceRoleConstants.Admin)));
        return builder;
    }

    /// <summary>Maps the account API (<see cref="AccountPath"/>) with its sign-in declaration, the organization management API
    /// (<see cref="OrganizationsPath"/>), and the OIDC server's pass-through endpoints.</summary>
    private static WebApplication MapIdentityEndpoints(this WebApplication app)
    {
        var account = app.MapGroup(AccountPath);
        account.MapUserAccountEndpoints();
        account.MapSignInOptionsEndpoint();
        app.MapGroup(OrganizationsPath).MapOrganizationEndpoints();
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

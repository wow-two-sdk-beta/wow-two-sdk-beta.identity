using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using WoW.Two.Sdk.Beta.Be.Identity.Bff.Antiforgery;
using WoW.Two.Sdk.Beta.Be.Identity.Bff.Authorization;
using WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;
using WoW.Two.Sdk.Beta.Be.Identity.Cookies;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff;

/// <summary>Registers the BFF sign-in against an Identity instance.</summary>
public static class IdentityBffServiceCollectionExtensions
{
    /// <summary>
    /// Register a BFF's sign-in through an Identity instance by the OpenID Connect code flow with PKCE: the settings
    /// (<see cref="IdentityBffOptions"/>, host section <c>Identity:Client</c>, then <paramref name="configure"/>, checked at
    /// boot), the session cookie as the default scheme, server-side sessions behind <see cref="IBffSessionRepository"/>
    /// (the distributed cache by default) and the organization policies. Add <c>UseIdentityBff</c> to the pipeline and
    /// map the session routes with <c>MapIdentityBffEndpoints</c>.
    /// </summary>
    /// <remarks>
    ///   - the cookie carries only the session reference; tokens and claims stay server-side, encrypted with Data
    ///     Protection, keyed on the instance's <c>sid</c> — several instances share the store and the key ring
    ///   - API requests (<see cref="IdentityBffOptions.ApiPathPrefixes"/>) answer 401 and 403 problems; a page request
    ///     without a session is redirected to <c>{prefix}/sign-in</c>; the session routes sit under <c>/api</c> by
    ///     default, and <c>{prefix}/sign-in</c> and <c>{prefix}/callback</c> still redirect, as a browser navigates to them
    ///   - the session refreshes its tokens and ends with the instance's sign-in session (<see cref="IdentityBffOptions.SessionLifetime"/>
    ///     when the instance names no end)
    ///   - unsafe API requests of a session need <c>X-Csrf-Token</c>, bound to the session's <c>sid</c>
    ///   - the defaults keep sessions in the in-memory distributed cache and the key ring in the process's own folder, enough
    ///     for a local run only: a deployed host registers a shared <c>IDistributedCache</c> and a persisted key ring, or every
    ///     restart signs everyone out; a deployed boot (<c>DEPLOY_ENVIRONMENT</c> set) logs a warning for each one missing
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">Code configuration, applied after the host section, so it wins.</param>
    public static IServiceCollection AddIdentityBff(this IServiceCollection services, Action<IdentityBffOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.AddOptions<IdentityBffOptions>().BindConfiguration(IdentityBffOptions.SectionName);
        if (configure is not null)
            options.Configure(configure);
        options.ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<IdentityBffOptions>, IdentityBffOptionsValidator>());

        services.TryAddSingleton(TimeProvider.System);
        services.AddDataProtection();
        services.AddDistributedMemoryCache();
        services.AddHttpContextAccessor();
        services.TryAddSingleton<IBffSessionRepository, DistributedCacheBffSessionRepository>();
        services.TryAddSingleton<BffSessionProtector>();
        services.TryAddSingleton<BffTokenClient>();
        services.TryAddSingleton<BffSessionService>();
        services.TryAddSingleton<BffCsrfTokenService>();
        services.TryAddTransient<BffCookieEvents>();
        services.TryAddTransient<BffOpenIdConnectEvents>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, BffStorageCheck>());

        services.AddAuthentication(authentication =>
            {
                authentication.DefaultScheme = BffAuthenticationSchemeConstants.Cookie;
                authentication.DefaultChallengeScheme = BffAuthenticationSchemeConstants.Cookie;
                authentication.DefaultForbidScheme = BffAuthenticationSchemeConstants.Cookie;
                authentication.DefaultSignInScheme = BffAuthenticationSchemeConstants.Cookie;
                authentication.DefaultSignOutScheme = BffAuthenticationSchemeConstants.Cookie;
            })
            .AddCookie(BffAuthenticationSchemeConstants.Cookie)
            .AddOpenIdConnect(BffAuthenticationSchemeConstants.OpenIdConnect, static _ => { });

        // Read late, so a host's configuration (a test host's included) reaches the handlers.
        services.AddOptions<CookieAuthenticationOptions>(BffAuthenticationSchemeConstants.Cookie)
            .Configure<IOptions<IdentityBffOptions>, IDataProtectionProvider>(static (cookie, bff, protection) => ConfigureCookie(cookie, bff.Value, protection));
        services.AddOptions<OpenIdConnectOptions>(BffAuthenticationSchemeConstants.OpenIdConnect)
            .Configure<IOptions<IdentityBffOptions>>(static (handler, bff) => ConfigureOpenIdConnect(handler, bff.Value));

        services.AddAuthorization();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, OrganizationPermissionAuthorizationHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, RouteOrganizationAuthorizationHandler>());
        DecorateAuthorizationResultHandler(services);
        return services;
    }

    private static void ConfigureCookie(CookieAuthenticationOptions cookie, IdentityBffOptions bff, IDataProtectionProvider protection)
    {
        cookie.Cookie.Name = EnvironmentCookieName.For(bff.CookieName, bff.Environment, bff.PrimaryEnvironment);
        cookie.Cookie.HttpOnly = true;
        cookie.Cookie.SameSite = SameSiteMode.Lax;
        cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        cookie.ExpireTimeSpan = bff.SessionLifetime;
        cookie.SlidingExpiration = false;
        cookie.LoginPath = bff.RoutePrefix + BffPathConstants.SignIn;
        cookie.LogoutPath = bff.RoutePrefix + BffPathConstants.SignOut;
        cookie.ReturnUrlParameter = "returnUrl";
        cookie.TicketDataFormat = new BffTicketDataFormat(new TicketDataFormat(
            protection.CreateProtector("WoW.Two.Sdk.Beta.Be.Identity.Bff.Cookie", BffAuthenticationSchemeConstants.Cookie, "v1")));
        cookie.EventsType = typeof(BffCookieEvents);
    }

    private static void ConfigureOpenIdConnect(OpenIdConnectOptions handler, IdentityBffOptions bff)
    {
        handler.Authority = bff.Authority;
        handler.ClientId = bff.ClientId;
        handler.ClientSecret = bff.ClientSecret;
        handler.RequireHttpsMetadata = bff.IsHttpsMetadataRequired;
        handler.ResponseType = OpenIdConnectResponseType.Code;
        handler.ResponseMode = OpenIdConnectResponseMode.Query;
        handler.UsePkce = true;
        handler.SaveTokens = true;
        handler.MapInboundClaims = false;
        handler.GetClaimsFromUserInfoEndpoint = false;
        handler.DisableTelemetry = true;
        handler.SignInScheme = BffAuthenticationSchemeConstants.Cookie;
        handler.SignOutScheme = BffAuthenticationSchemeConstants.Cookie;
        handler.CallbackPath = bff.RoutePrefix + BffPathConstants.Callback;
        handler.SignedOutCallbackPath = bff.RoutePrefix + BffPathConstants.SignedOutCallback;
        handler.RemoteSignOutPath = bff.RoutePrefix + BffPathConstants.FrontChannelSignOut;
        handler.TokenValidationParameters.NameClaimType = JwtRegisteredClaimNames.Name;
        handler.TokenValidationParameters.RoleClaimType = "role";
        handler.Scope.Clear();
        foreach (var scope in bff.EffectiveScopes)
            handler.Scope.Add(scope);
        handler.EventsType = typeof(BffOpenIdConnectEvents);
    }

    /// <summary>
    /// Wrap the registered authorization result handler (the framework's default when none) with
    /// <see cref="BffAuthorizationResultHandler"/>, once, so a failed route-organization check answers 404.
    /// </summary>
    private static void DecorateAuthorizationResultHandler(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(BffAuthorizationResultHandlerMarker)))
            return;

        var inner = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IAuthorizationMiddlewareResultHandler) && !descriptor.IsKeyedService);
        services.RemoveAll<IAuthorizationMiddlewareResultHandler>();
        services.AddSingleton<BffAuthorizationResultHandlerMarker>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler>(provider => new BffAuthorizationResultHandler(inner switch
        {
            null => new AuthorizationMiddlewareResultHandler(),
            { ImplementationInstance: IAuthorizationMiddlewareResultHandler instance } => instance,
            { ImplementationFactory: { } factory } => (IAuthorizationMiddlewareResultHandler)factory(provider),
            { ImplementationType: { } type } => (IAuthorizationMiddlewareResultHandler)ActivatorUtilities.CreateInstance(provider, type),
            _ => new AuthorizationMiddlewareResultHandler(),
        }));
    }

    /// <summary>Marks a service collection whose authorization result handler is already wrapped.</summary>
    private sealed class BffAuthorizationResultHandlerMarker;
}

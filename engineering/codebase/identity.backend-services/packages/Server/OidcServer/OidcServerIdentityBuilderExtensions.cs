using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenIddict.Server;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;
using WoW.Two.Sdk.Beta.Be.Identity.Keys;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

/// <summary>Turns an identity registration into an OpenID Connect provider.</summary>
public static class OidcServerIdentityBuilderExtensions
{
    /// <summary>
    /// Register the OpenID Connect server over the Module's store: OpenIddict's server with its EF Core stores on
    /// <see cref="IdentityDbContext"/>, the authorization code flow with PKCE (S256 only) and rotating refresh tokens,
    /// discovery, JWKS, userinfo and end-session; keys from <see cref="ISigningKeySource"/> (the settings by default),
    /// checked at boot; and the clients of <see cref="OidcServerOptions.Clients"/>, seeded at startup. Map the
    /// pass-through endpoints with <c>MapOidcServerEndpoints</c> behind <c>UseAuthentication</c>.
    /// </summary>
    /// <remarks>
    ///   - no implicit, password or client-credentials grant; access tokens are unencrypted JWTs that resource servers
    ///     verify offline against the JWKS, while codes and refresh tokens stay encrypted
    ///   - the authorization endpoint reads the cookie sign-in session (<c>MapUserAccountEndpoints</c> with
    ///     <c>useCookies</c>) and sends a person without one to <see cref="OidcServerOptions.SignInPath"/>
    ///   - with the sessions slice, every grant hangs off the sign-in session's <c>sid</c>: the tokens carry it, the token
    ///     and userinfo endpoints refuse an ended session, and revoking the session revokes its grants and tokens
    ///   - a boot fails closed on unusable keys, and on ephemeral keys outside a local run
    /// </remarks>
    /// <param name="builder">The identity builder from <c>AddUserAccounts</c>.</param>
    /// <param name="configure">Optional server options; the host section <c>Identity:Server</c> is applied last.</param>
    public static IdentityBuilder AddOidcServer(this IdentityBuilder builder, Action<OidcServerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        services.AddModuleOptions(OidcServerOptions.SectionName, configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<OidcServerOptions>, OidcServerOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISigningKeySource, SettingsSigningKeySource>();
        services.TryAddScoped<OidcClaimMapper>();
        services.TryAddScoped<OidcClientSeeder>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ISessionRevocationHandler, OidcSessionRevocationHandler>());
        services.AddHostedService<OidcClientSeedingService>();

        services.AddOpenIddict()
            .AddCore(core => core.UseEntityFrameworkCore()
                .UseDbContext<IdentityDbContext>()
                .ReplaceDefaultEntities<IdentityOidcApplication, IdentityOidcAuthorization, IdentityOidcScope, IdentityOidcToken, Guid>())
            .AddServer(server =>
            {
                server.SetAuthorizationEndpointUris(Relative(OidcServerPathConstants.Authorization))
                    .SetTokenEndpointUris(Relative(OidcServerPathConstants.Token))
                    .SetUserInfoEndpointUris(Relative(OidcServerPathConstants.UserInfo))
                    .SetEndSessionEndpointUris(Relative(OidcServerPathConstants.EndSession))
                    .SetConfigurationEndpointUris(Relative(OidcServerPathConstants.Configuration))
                    .SetJsonWebKeySetEndpointUris(Relative(OidcServerPathConstants.JsonWebKeySet));

                server.AllowAuthorizationCodeFlow()
                    .AllowRefreshTokenFlow()
                    .RequireProofKeyForCodeExchange()
                    .DisableAccessTokenEncryption()
                    .DisableSlidingRefreshTokenExpiration();

                server.RegisterScopes(Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.OfflineAccess);
                server.RegisterClaims(
                    Claims.Subject, SessionClaimTypeConstants.SessionId, Claims.Name, Claims.Email, Claims.EmailVerified, Claims.AuthenticationMethodReference,
                    Claims.AuthenticationTime, OrganizationClaimTypeConstants.Id, OrganizationClaimTypeConstants.Role,
                    OrganizationClaimTypeConstants.Permission);

                server.AddEventHandler(ActiveKeySelectionHandler.Descriptor);
                server.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough();
            });

        // Settings read late, so a host's configuration (a test host's included) reaches them; the keys check runs at boot.
        services.AddOptions<OpenIddictServerOptions>()
            .Configure<OidcServerOptions>(static (server, options) =>
            {
                server.Issuer = options.Issuer;
                server.AuthorizationCodeLifetime = options.AuthorizationCodeLifetime;
                server.AccessTokenLifetime = options.AccessTokenLifetime;
                server.IdentityTokenLifetime = options.IdentityTokenLifetime;
                server.RefreshTokenLifetime = options.RefreshTokenLifetime;
                server.CodeChallengeMethods.Clear();
                server.CodeChallengeMethods.Add(CodeChallengeMethods.Sha256);
            })
            .ValidateOnStart();
        services.AddSingleton<IConfigureOptions<OpenIddictServerOptions>, ServerKeyConfiguration>();
        return builder;
    }

    /// <summary>OpenIddict takes endpoint paths relative to the issuer, without the leading slash.</summary>
    private static string Relative(string path) => path.TrimStart('/');
}

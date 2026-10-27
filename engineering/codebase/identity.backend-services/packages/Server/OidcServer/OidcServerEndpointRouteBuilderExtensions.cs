using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

/// <summary>Maps the OpenID Connect server's pass-through endpoints: authorize, token, userinfo and end-session.</summary>
public static class OidcServerEndpointRouteBuilderExtensions
{
    /// <summary>The parameters dropped from the return URL, so the sign-in it leads back to is not asked for again.</summary>
    private static readonly HashSet<string> FreshSignInParameters = new(StringComparer.Ordinal) { Parameters.Prompt, Parameters.MaxAge };

    /// <summary>
    /// Map the endpoints OpenIddict validates and hands to our logic: <see cref="OidcServerPathConstants.Authorization"/>
    /// signs in the person of the cookie session (or sends them to <see cref="OidcServerOptions.SignInPath"/>) and ties
    /// the grant to that session's <c>sid</c>, <see cref="OidcServerPathConstants.Token"/> re-checks the person and the
    /// session and refreshes their claims, <see cref="OidcServerPathConstants.UserInfo"/> answers their current profile
    /// and <see cref="OidcServerPathConstants.EndSession"/> ends the session and the app's tokens. Discovery and the JWKS
    /// are OpenIddict's own. Map them on the root, behind <c>UseAuthentication</c>.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    public static IEndpointRouteBuilder MapOidcServerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        string[] getOrPost = [HttpMethods.Get, HttpMethods.Post];
        endpoints.MapMethods(OidcServerPathConstants.Authorization, getOrPost, AuthorizeAsync).AllowAnonymous().ExcludeFromDescription();
        endpoints.MapPost(OidcServerPathConstants.Token, ExchangeAsync).AllowAnonymous().ExcludeFromDescription();
        endpoints.MapMethods(OidcServerPathConstants.UserInfo, getOrPost, UserInfoAsync).AllowAnonymous().ExcludeFromDescription();
        endpoints.MapMethods(OidcServerPathConstants.EndSession, getOrPost, EndSessionAsync).AllowAnonymous().ExcludeFromDescription();
        return endpoints;
    }

    private static async Task<IResult> AuthorizeAsync(HttpContext http, CancellationToken cancellationToken)
    {
        var request = RequestOf(http);
        var services = http.RequestServices;
        var time = services.GetService<TimeProvider>() ?? TimeProvider.System;
        var session = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var user = session.Succeeded ? await FindPersonAsync(services, session.Principal!, sessionClaims: true, time, cancellationToken) : null;
        var authenticatedAt = session.Properties?.IssuedUtc;

        // prompt=login and an exceeded max_age ask for a fresh sign-in; max_age=0 always does.
        var stale = request.HasPromptValue(PromptValues.Login)
            || (request.MaxAge is { } maxAge && (authenticatedAt is not { } at || time.GetUtcNow() - at > TimeSpan.FromSeconds(maxAge)));
        if (user is null || stale)
        {
            return request.HasPromptValue(PromptValues.None)
                ? Forbid(Errors.LoginRequired, "The person is not signed in.")
                : Results.Redirect(await SignInUrlAsync(http, services.GetRequiredService<OidcServerOptions>().SignInPath));
        }

        var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
        var client = await OidcClientProfile.FindAsync(applications, request.ClientId, cancellationToken)
            ?? throw new InvalidOperationException("The validated authorization request names no registered client.");
        var sessionId = SessionIdOf(session.Principal!, services.GetRequiredService<IdentityCoreOptions>().Claims.SessionIdClaimType);
        var identity = OidcClaimMapper.CreateIdentity(
            client,
            request.GetScopes(),
            session.Principal!.FindAll(AuthenticationMethodConstants.ClaimType).Select(claim => claim.Value),
            authenticatedAt,
            sessionId);
        var principal = await services.GetRequiredService<OidcClaimMapper>().ApplyAsync(identity, user, client, cancellationToken);

        // The grant hangs off an authorization naming the session, so revoking the session ends its tokens.
        if (sessionId is { } sid
            && await applications.FindByClientIdAsync(client.ClientId, cancellationToken) is { } application
            && await applications.GetIdAsync(application, cancellationToken) is { } applicationId)
        {
            await OidcSessionRevocationHandler.AttachAsync(
                services.GetRequiredService<IOpenIddictAuthorizationManager>(), applicationId, user.Id.ToString("D"), sid, principal, time.GetUtcNow(), cancellationToken);
        }

        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> ExchangeAsync(HttpContext http, CancellationToken cancellationToken)
    {
        var request = RequestOf(http);
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
            return Forbid(Errors.UnsupportedGrantType, "Only the authorization code and refresh token grants are served.");

        // The principal sealed in the code or refresh token: its scopes, audience, methods, authorization and stamp.
        var services = http.RequestServices;
        var time = services.GetService<TimeProvider>() ?? TimeProvider.System;
        var grant = await http.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var user = grant.Principal is { } sealedPrincipal ? await FindPersonAsync(services, sealedPrincipal, sessionClaims: false, time, cancellationToken) : null;
        var client = await OidcClientProfile.FindAsync(services.GetRequiredService<IOpenIddictApplicationManager>(), request.ClientId, cancellationToken);
        if (user is null || client is null)
            return Forbid(Errors.InvalidGrant, "The sign-in behind this grant is no longer valid.");
        if (!await IsSessionLiveAsync(services, grant.Principal!, user, cancellationToken))
            return Forbid(Errors.InvalidGrant, "The sign-in session behind this grant has ended.");

        var identity = new ClaimsIdentity(grant.Principal!.Claims, grant.Principal.Identity?.AuthenticationType, Claims.Name, Claims.Role);
        var principal = await services.GetRequiredService<OidcClaimMapper>().ApplyAsync(identity, user, client, cancellationToken);
        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> UserInfoAsync(HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var time = services.GetService<TimeProvider>() ?? TimeProvider.System;
        var token = await http.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var user = token.Principal is { } accessToken ? await FindPersonAsync(services, accessToken, sessionClaims: false, time, cancellationToken) : null;
        var clientId = token.Principal?.GetPresenters().FirstOrDefault() ?? token.Principal?.GetClaim(Claims.ClientId);
        var client = await OidcClientProfile.FindAsync(services.GetRequiredService<IOpenIddictApplicationManager>(), clientId, cancellationToken);
        if (user is null || client is null || !await IsSessionLiveAsync(services, token.Principal!, user, cancellationToken))
        {
            return Results.Challenge(
                Properties(Errors.InvalidToken, "The access token no longer names a person."),
                [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        var identity = new ClaimsIdentity(token.Principal!.Claims, token.Principal.Identity?.AuthenticationType, Claims.Name, Claims.Role);
        var principal = await services.GetRequiredService<OidcClaimMapper>().ApplyAsync(identity, user, client, cancellationToken);
        return Results.Ok(OidcClaimMapper.ToUserInfo(principal));
    }

    private static async Task<IResult> EndSessionAsync(HttpContext http, CancellationToken cancellationToken)
    {
        var request = RequestOf(http);
        var services = http.RequestServices;
        if (await services.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync(CookieAuthenticationDefaults.AuthenticationScheme) is not null)
        {
            // The cookie's session ends first, and with it every grant issued under it.
            var cookie = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            var claims = services.GetRequiredService<IdentityCoreOptions>().Claims;
            if (cookie.Principal is { } signedIn
                && services.GetService<UserSessionService>() is { } sessions
                && SessionIdOf(signedIn, claims.SessionIdClaimType) is { } sid
                && Guid.TryParse(signedIn.FindFirst(claims.UserIdClaimType)?.Value, out var userId))
                await sessions.RevokeAsync(userId, sid, SessionRevokeReasonConstants.SignOut, cancellationToken);

            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        // With an id_token_hint, the app's tokens for that person end too: its refresh tokens stop working at once.
        var hint = await http.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (hint.Principal?.GetClaim(Claims.Subject) is { } subject)
        {
            var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
            var authorizations = services.GetRequiredService<IOpenIddictAuthorizationManager>();
            var tokens = services.GetRequiredService<IOpenIddictTokenManager>();
            foreach (var clientId in hint.Principal.GetAudiences().Concat(hint.Principal.GetPresenters()).Append(request.ClientId).OfType<string>().Distinct(StringComparer.Ordinal))
            {
                if (await applications.FindByClientIdAsync(clientId, cancellationToken) is not { } application
                    || await applications.GetIdAsync(application, cancellationToken) is not { } applicationId)
                    continue;

                await authorizations.RevokeAsync(subject, applicationId, status: null, type: null, cancellationToken);
                await tokens.RevokeAsync(subject, applicationId, status: null, type: null, cancellationToken);
            }
        }

        // OpenIddict redirects to the validated post_logout_redirect_uri, or here when the request named none.
        return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    /// <summary>
    /// The person a session or a sealed grant names, while they may still sign in: the account exists, is not locked out
    /// and carries the same security stamp. A session names them by the Module's claim type, a grant by <c>sub</c>.
    /// </summary>
    private static async Task<IdentityUser?> FindPersonAsync(IServiceProvider services, ClaimsPrincipal principal, bool sessionClaims, TimeProvider time, CancellationToken cancellationToken)
    {
        var claims = services.GetRequiredService<IdentityCoreOptions>().Claims;
        var id = principal.FindFirst(sessionClaims ? claims.UserIdClaimType : Claims.Subject)?.Value;
        if (!Guid.TryParse(id, out var userId)
            || await services.GetRequiredService<UserAccountService>().FindByIdAsync(userId, cancellationToken) is not { } user)
            return null;

        if (user.LockoutEnd is { } lockoutEnd && lockoutEnd > time.GetUtcNow())
            return null;

        var stamp = principal.FindFirst(claims.SecurityStampClaimType)?.Value;
        return stamp is null || string.Equals(stamp, user.SecurityStamp, StringComparison.Ordinal) ? user : null;
    }

    /// <summary>
    /// Whether the sign-in session a grant or token names is still live; a principal without <c>sid</c> (issued before
    /// sessions) and a host without the sessions slice pass.
    /// </summary>
    private static async Task<bool> IsSessionLiveAsync(IServiceProvider services, ClaimsPrincipal principal, IdentityUser user, CancellationToken cancellationToken)
    {
        var presented = principal.GetClaim(SessionClaimTypeConstants.SessionId);
        if (presented is null || services.GetService<UserSessionService>() is not { } sessions)
            return true;

        return Guid.TryParse(presented, out var sid) && await sessions.ValidateAsync(sid, user.Id, cancellationToken);
    }

    /// <summary>The session id a cookie principal carries under <paramref name="claimType"/>, or null.</summary>
    private static Guid? SessionIdOf(ClaimsPrincipal principal, string claimType)
        => Guid.TryParse(principal.FindFirst(claimType)?.Value, out var sid) ? sid : null;

    /// <summary>The sign-in page's URL, returning to this authorization request without the parameters that asked for a fresh sign-in.</summary>
    private static async Task<string> SignInUrlAsync(HttpContext http, string signInPath)
    {
        IEnumerable<KeyValuePair<string, StringValues>> parameters = http.Request.HasFormContentType
            ? await http.Request.ReadFormAsync(http.RequestAborted)
            : http.Request.Query;
        var returnUrl = http.Request.PathBase + http.Request.Path
            + QueryString.Create(parameters.Where(parameter => !FreshSignInParameters.Contains(parameter.Key)));
        return $"{signInPath}?returnUrl={Uri.EscapeDataString(returnUrl)}";
    }

    private static OpenIddictRequest RequestOf(HttpContext http)
        => http.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("No OpenID Connect request reached this endpoint; map it behind UseAuthentication with AddOidcServer registered.");

    private static IResult Forbid(string error, string description)
        => Results.Forbid(Properties(error, description), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

    private static AuthenticationProperties Properties(string error, string description)
        => new(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
        });
}

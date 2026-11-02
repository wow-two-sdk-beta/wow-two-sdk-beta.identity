using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
    /// <summary>
    /// The token response parameter naming, in seconds, when the refresh chain ends: with the sign-in session behind it.
    /// Not in RFC 6749; the name others use for it.
    /// </summary>
    public const string RefreshTokenExpiresInParameter = "refresh_token_expires_in";

    /// <summary>
    /// The return URL parameter that replaces <c>prompt=login</c> and <c>max_age</c> on the way to the sign-in page: the
    /// instant the fresh sign-in was asked for, sealed with Data Protection, so the request it leads back to passes only
    /// for a session signed in after it.
    /// </summary>
    public const string FreshSignInParameter = "fresh_sign_in";

    /// <summary>The Data Protection purpose that seals <see cref="FreshSignInParameter"/>.</summary>
    private const string FreshSignInPurpose = "WoW.Two.Sdk.Beta.Be.Identity.OidcServer.FreshSignIn.v1";

    /// <summary>The parameters dropped from the return URL, so the sign-in it leads back to is not asked for again; the sealed marker stands in for them.</summary>
    private static readonly HashSet<string> FreshSignInParameters = new(StringComparer.Ordinal) { Parameters.Prompt, Parameters.MaxAge, FreshSignInParameter };

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
        var now = time.GetUtcNow();
        var session = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var user = session.Succeeded ? await FindPersonAsync(services, session.Principal!, sessionClaims: true, cancellationToken) : null;
        var sessionId = session.Principal is { } cookie ? SessionIdOf(cookie, services.GetRequiredService<IdentityCoreOptions>().Claims.SessionIdClaimType) : null;
        (bool IsLive, DateTimeOffset? At) signedIn = user is null ? (false, null) : await AuthenticatedAtAsync(services, user, sessionId, session.Properties, cancellationToken);
        var authenticatedAt = signedIn.At;
        if (!signedIn.IsLive)
            user = null;

        // prompt=login and an exceeded max_age ask for a fresh sign-in, max_age=0 always; the sealed marker of an earlier
        // such request, back from the sign-in page, asks for a session signed in after it was made.
        var protector = services.GetRequiredService<IDataProtectionProvider>().CreateProtector(FreshSignInPurpose);
        var stale = request.HasPromptValue(PromptValues.Login)
            || (request.MaxAge is { } maxAge && (authenticatedAt is not { } at || now - at > TimeSpan.FromSeconds(maxAge)))
            || (HasParameter(request, FreshSignInParameter) && !IsSignedInAfter(authenticatedAt, ReadFreshSignIn(protector, request)));
        if (user is null || stale)
        {
            return request.HasPromptValue(PromptValues.None)
                ? Forbid(Errors.LoginRequired, "The person is not signed in.")
                : Results.Redirect(await SignInUrlAsync(http, services.GetRequiredService<OidcServerOptions>().SignInPath, stale ? protector.Protect(SealFreshSignIn(now)) : null));
        }

        var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
        var client = await OidcClientProfile.FindAsync(applications, request.ClientId, cancellationToken)
            ?? throw new InvalidOperationException("The validated authorization request names no registered client.");
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
        var user = grant.Principal is { } sealedPrincipal ? await FindPersonAsync(services, sealedPrincipal, sessionClaims: false, cancellationToken) : null;
        var client = await OidcClientProfile.FindAsync(services.GetRequiredService<IOpenIddictApplicationManager>(), request.ClientId, cancellationToken);
        if (user is null || client is null)
            return Forbid(Errors.InvalidGrant, "The sign-in behind this grant is no longer valid.");
        var (isLive, session) = await SessionOfAsync(services, grant.Principal!, user, cancellationToken);
        if (!isLive)
            return Forbid(Errors.InvalidGrant, "The sign-in session behind this grant has ended.");

        var identity = new ClaimsIdentity(grant.Principal!.Claims, grant.Principal.Identity?.AuthenticationType, Claims.Name, Claims.Role);
        var principal = await services.GetRequiredService<OidcClaimMapper>().ApplyAsync(identity, user, client, cancellationToken);
        var accessTokenLifetime = services.GetRequiredService<IOptions<OidcServerOptions>>().Value.AccessTokenLifetime;
        var properties = EndWithSession(principal, grant.Principal, session, request, accessTokenLifetime, time.GetUtcNow());
        return Results.SignIn(principal, properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Ends the refresh chain with the sign-in session: each new refresh token lives until the session's end (moved by a
    /// sliding session's use), and the response names that end as <see cref="RefreshTokenExpiresInParameter"/>, so a client
    /// such as the BFF ends its own session with it; no access token outlives the session. A grant without a session keeps
    /// the chain's absolute end.
    /// </summary>
    private static AuthenticationProperties EndWithSession(
        ClaimsPrincipal principal,
        ClaimsPrincipal grant,
        IdentitySession? session,
        OpenIddictRequest request,
        TimeSpan accessTokenLifetime,
        DateTimeOffset now)
    {
        var properties = new AuthenticationProperties();
        var end = session?.ExpiresAt ?? (request.IsRefreshTokenGrantType() ? grant.GetExpirationDate() : null);
        if (end is not { } chainEnd || chainEnd <= now)
            return properties;

        var remaining = chainEnd - now;
        principal.SetRefreshTokenLifetime(remaining);
        if (remaining < accessTokenLifetime)
            principal.SetAccessTokenLifetime(remaining);
        if (principal.HasScope(Scopes.OfflineAccess))
            properties.Parameters[RefreshTokenExpiresInParameter] = (long)remaining.TotalSeconds;

        return properties;
    }

    private static async Task<IResult> UserInfoAsync(HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var token = await http.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var user = token.Principal is { } accessToken ? await FindPersonAsync(services, accessToken, sessionClaims: false, cancellationToken) : null;
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

    /// <summary>
    /// Ends the sign-in only when the request proves it comes from the person's own app or page: a valid
    /// <c>id_token_hint</c> (OpenIddict refuses an invalid one), or a <c>POST</c> from this origin. Anything else, such as a
    /// bare <c>GET</c> another site's link or image sends with the cookie, only redirects, so no site can sign the person out.
    /// </summary>
    private static async Task<IResult> EndSessionAsync(HttpContext http, CancellationToken cancellationToken)
    {
        var request = RequestOf(http);
        var services = http.RequestServices;
        var hint = await http.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var subject = hint.Principal?.GetClaim(Claims.Subject);
        if (subject is null && !IsSameOriginPost(http))
            return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

        if (await services.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync(CookieAuthenticationDefaults.AuthenticationScheme) is not null)
        {
            // The cookie's session ends first, and with it every grant issued under it; a hint naming someone else leaves it.
            var cookie = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            var claims = services.GetRequiredService<IdentityCoreOptions>().Claims;
            var cookieUserId = cookie.Principal?.FindFirst(claims.UserIdClaimType)?.Value;
            if (subject is null || cookieUserId is null || string.Equals(subject, cookieUserId, StringComparison.OrdinalIgnoreCase))
            {
                if (cookie.Principal is { } signedIn
                    && services.GetService<UserSessionService>() is { } sessions
                    && SessionIdOf(signedIn, claims.SessionIdClaimType) is { } sid
                    && Guid.TryParse(cookieUserId, out var userId))
                    await sessions.RevokeAsync(userId, sid, SessionRevokeReasonConstants.SignOut, cancellationToken);

                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        }

        // With an id_token_hint, the app's tokens for that person end too: its refresh tokens stop working at once.
        if (subject is not null)
        {
            var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
            var authorizations = services.GetRequiredService<IOpenIddictAuthorizationManager>();
            var tokens = services.GetRequiredService<IOpenIddictTokenManager>();
            foreach (var clientId in hint.Principal!.GetAudiences().Concat(hint.Principal!.GetPresenters()).Append(request.ClientId).OfType<string>().Distinct(StringComparer.Ordinal))
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
    /// The person a session or a sealed grant names, while their sign-in still holds: the account exists and carries the
    /// same security stamp. A session names them by the Module's claim type, a grant by <c>sub</c>. A lockout is no reason
    /// to end it: it refuses new credential attempts only, so failed guesses by someone else never sign the person out of
    /// their apps; ending sessions takes a stamp rotation or a session revocation.
    /// </summary>
    private static async Task<IdentityUser?> FindPersonAsync(IServiceProvider services, ClaimsPrincipal principal, bool sessionClaims, CancellationToken cancellationToken)
    {
        var claims = services.GetRequiredService<IdentityCoreOptions>().Claims;
        var id = principal.FindFirst(sessionClaims ? claims.UserIdClaimType : Claims.Subject)?.Value;
        if (!Guid.TryParse(id, out var userId)
            || await services.GetRequiredService<UserAccountService>().FindByIdAsync(userId, cancellationToken) is not { } user)
            return null;

        var stamp = principal.FindFirst(claims.SecurityStampClaimType)?.Value;
        return stamp is null || string.Equals(stamp, user.SecurityStamp, StringComparison.Ordinal) ? user : null;
    }

    /// <summary>
    /// When the cookie's person signed in: the sign-in session's start, which no sliding reissue moves, or the cookie's
    /// issue time on a host without sessions or for a cookie without <c>sid</c>. Not live when the session ended.
    /// </summary>
    private static async Task<(bool IsLive, DateTimeOffset? AuthenticatedAt)> AuthenticatedAtAsync(
        IServiceProvider services, IdentityUser user, Guid? sessionId, AuthenticationProperties? cookie, CancellationToken cancellationToken)
    {
        if (sessionId is not { } sid || services.GetService<UserSessionService>() is not { } sessions)
            return (true, cookie?.IssuedUtc);

        return await sessions.FindLiveAsync(sid, user.Id, cancellationToken) is { } live ? (true, live.CreatedAt) : (false, null);
    }

    /// <summary>Whether the request is a <c>POST</c> a page of this origin sent: <c>Sec-Fetch-Site: same-origin</c>, or else an <c>Origin</c> equal to this one.</summary>
    private static bool IsSameOriginPost(HttpContext http)
    {
        var request = http.Request;
        if (!HttpMethods.IsPost(request.Method))
            return false;
        if (request.Headers["Sec-Fetch-Site"].ToString() is { Length: > 0 } site)
            return string.Equals(site, "same-origin", StringComparison.OrdinalIgnoreCase);

        var origin = request.Headers.Origin.ToString();
        return origin.Length > 0 && string.Equals(origin, $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasParameter(OpenIddictRequest request, string name) => request.GetParameter(name) is not null;

    /// <summary>The instant a valid <see cref="FreshSignInParameter"/> names, or null when it is missing, forged or unreadable.</summary>
    private static DateTimeOffset? ReadFreshSignIn(IDataProtector protector, OpenIddictRequest request)
    {
        if ((string?)request.GetParameter(FreshSignInParameter) is not { Length: > 0 } sealedValue)
            return null;

        try
        {
            return long.TryParse(protector.Unprotect(sealedValue), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                ? new DateTimeOffset(ticks, TimeSpan.Zero)
                : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static string SealFreshSignIn(DateTimeOffset at) => at.UtcTicks.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether the session signed in no earlier than the second the fresh sign-in was asked for; <c>auth_time</c> counts in
    /// whole seconds, and a stored session start keeps microseconds only. A missing session or marker never passes.
    /// </summary>
    private static bool IsSignedInAfter(DateTimeOffset? signedInAt, DateTimeOffset? askedAt)
        => signedInAt is { } at && askedAt is { } asked && at.ToUnixTimeSeconds() >= asked.ToUnixTimeSeconds();

    /// <summary>
    /// Whether the sign-in session a grant or token names is still live; a principal without <c>sid</c> (issued before
    /// sessions) and a host without the sessions slice pass.
    /// </summary>
    private static async Task<bool> IsSessionLiveAsync(IServiceProvider services, ClaimsPrincipal principal, IdentityUser user, CancellationToken cancellationToken)
        => (await SessionOfAsync(services, principal, user, cancellationToken)).IsLive;

    /// <summary>The live sign-in session a grant or token names, its use recorded; live with no session when it names none.</summary>
    private static async Task<(bool IsLive, IdentitySession? Session)> SessionOfAsync(IServiceProvider services, ClaimsPrincipal principal, IdentityUser user, CancellationToken cancellationToken)
    {
        var presented = principal.GetClaim(SessionClaimTypeConstants.SessionId);
        if (presented is null || services.GetService<UserSessionService>() is not { } sessions)
            return (true, null);

        var session = Guid.TryParse(presented, out var sid) ? await sessions.FindLiveAsync(sid, user.Id, cancellationToken) : null;
        return (session is not null, session);
    }

    /// <summary>The session id a cookie principal carries under <paramref name="claimType"/>, or null.</summary>
    private static Guid? SessionIdOf(ClaimsPrincipal principal, string claimType)
        => Guid.TryParse(principal.FindFirst(claimType)?.Value, out var sid) ? sid : null;

    /// <summary>
    /// The sign-in page's URL, returning to this authorization request without the parameters that asked for a fresh sign-in;
    /// <paramref name="freshSignIn"/>, when the request asked for one, rides back as <see cref="FreshSignInParameter"/>.
    /// </summary>
    private static async Task<string> SignInUrlAsync(HttpContext http, string signInPath, string? freshSignIn)
    {
        IEnumerable<KeyValuePair<string, StringValues>> parameters = http.Request.HasFormContentType
            ? await http.Request.ReadFormAsync(http.RequestAborted)
            : http.Request.Query;
        var kept = parameters.Where(parameter => !FreshSignInParameters.Contains(parameter.Key)).ToList();
        if (freshSignIn is not null)
            kept.Add(new KeyValuePair<string, StringValues>(FreshSignInParameter, freshSignIn));
        var returnUrl = http.Request.PathBase + http.Request.Path + QueryString.Create(kept);
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

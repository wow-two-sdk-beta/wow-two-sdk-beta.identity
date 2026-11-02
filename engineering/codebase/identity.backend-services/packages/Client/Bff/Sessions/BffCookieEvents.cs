using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

/// <summary>
/// Binds the session cookie to the server-side session: a sign-in stores the session and leaves the cookie its
/// reference, each request resolves (and refreshes) the session behind it and reissues the cookie when the session's end
/// moved, a sign-out removes it; API requests answer
/// 401 and 403 problems where a page request is redirected to the sign-in route, unless a code callback failed moments
/// ago (<see cref="BffSignInFailure"/>), when it answers the 401 problem too.
/// </summary>
/// <param name="sessions">The session service.</param>
/// <param name="options">The BFF options.</param>
/// <param name="time">The clock a reissued cookie counts from.</param>
internal sealed class BffCookieEvents(BffSessionService sessions, IOptions<IdentityBffOptions> options, TimeProvider time) : CookieAuthenticationEvents
{
    /// <inheritdoc />
    public override async Task SigningIn(CookieSigningInContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var session = await sessions.StartAsync(context.Principal!, context.Properties, context.HttpContext.RequestAborted);

        // The tokens and the claims stay server-side; the cookie keeps the reference and the absolute end.
        context.Properties.StoreTokens([]);
        new BffSessionReference { SessionId = session.SessionId, Reference = session.State.Reference }.WriteTo(context.Properties);
        context.Properties.IssuedUtc = session.State.SignedInAt;
        context.Properties.ExpiresUtc = session.State.ExpiresAt;
        context.Properties.IsPersistent = true;
        context.Properties.AllowRefresh = false;
        context.Principal = BffSessionService.ReferencePrincipal();
    }

    /// <inheritdoc />
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var reference = BffSessionReference.From(context.Properties);
        var session = reference is null ? null : await sessions.ResolveAsync(reference, context.HttpContext.RequestAborted);
        if (session is null)
        {
            BffSessionService.MarkEnded(context.HttpContext);
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(BffAuthenticationSchemeConstants.Cookie);
            return;
        }

        context.ReplacePrincipal(BffSessionService.CreatePrincipal(session));
        context.Properties.StoreTokens(BffSessionService.TokensOf(session));

        // The instance's sliding session moved the end: reissue the cookie to end with it.
        var cookieEnd = context.Properties.ExpiresUtc;
        context.Properties.ExpiresUtc = session.State.ExpiresAt;
        if (cookieEnd is { } end && session.State.ExpiresAt > end)
        {
            context.Properties.IssuedUtc = time.GetUtcNow();
            context.ShouldRenew = true;
        }
    }

    /// <inheritdoc />
    public override async Task SigningOut(CookieSigningOutContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var http = context.HttpContext;
        if (BffSessionService.IsEnded(http))
            return;

        // A sign-out from elsewhere (the front-channel sign-out, the app's own SignOutAsync): end the session it names.
        var current = await http.AuthenticateAsync(BffAuthenticationSchemeConstants.Cookie);
        BffSessionService.MarkEnded(http);
        if (BffSessionReference.From(current.Properties) is { } reference)
            await sessions.EndAsync(reference.SessionId, http.RequestAborted);
    }

    /// <inheritdoc />
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (BffRequestMatcher.IsApi(context.Request, options.Value))
            return BffProblemWriter.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, BffErrorCodeConstants.SignInRequired, "Sign in to continue.");

        // A callback failed moments ago: answer the error rather than start the sign-in that failed again.
        if (BffSignInFailure.IsRecent(context.HttpContext, options.Value))
            return BffSignInFailure.WriteAsync(context.HttpContext);

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return BffProblemWriter.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden, BffErrorCodeConstants.AccessDenied, "The session lacks the permission this request needs.");
    }

    /// <inheritdoc />
    public override Task RedirectToLogout(RedirectContext<CookieAuthenticationOptions> context) => Task.CompletedTask;
}

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using WoW.Two.Sdk.Beta.Be.Identity.Cookies;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

/// <summary>
/// Marks a failed code callback for <see cref="Cooldown"/>, so a sign-in that keeps failing (a wrong client secret, clock
/// skew) stops at an error instead of looping through the instance: the sign-in route and a page's sign-in redirect
/// answer a <c>401</c> problem while the mark lasts, and the browser lands on its return URL carrying
/// <see cref="QueryParameter"/>, which the SPA can read before it asks to sign in again.
/// </summary>
internal static class BffSignInFailure
{
    /// <summary>The return URL's query parameter naming a failed sign-in.</summary>
    public const string QueryParameter = "signInError";

    /// <summary>The value of <see cref="QueryParameter"/>: the code callback failed.</summary>
    public const string CallbackFailed = "callback_failed";

    private const string Detail = "The sign-in could not be completed; try again in a few seconds.";

    /// <summary>How long a failed callback holds the next sign-in back; a person's own retry comes later than a redirect loop.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(10);

    /// <summary>Mark the failure on the browser and return <paramref name="returnUrl"/> carrying <see cref="QueryParameter"/>.</summary>
    /// <param name="http">The failed callback's request.</param>
    /// <param name="options">The BFF options, naming the session cookie the mark's cookie is named after.</param>
    /// <param name="returnUrl">The local path the browser goes back to.</param>
    public static string Mark(HttpContext http, IdentityBffOptions options, string returnUrl)
    {
        ArgumentNullException.ThrowIfNull(http);
        http.Response.Cookies.Append(CookieNameOf(options), "1", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = Cooldown,
            IsEssential = true,
        });
        return QueryHelpers.AddQueryString(returnUrl, QueryParameter, CallbackFailed);
    }

    /// <summary>Whether a code callback failed for this browser within <see cref="Cooldown"/>.</summary>
    /// <param name="http">The request.</param>
    /// <param name="options">The BFF options.</param>
    public static bool IsRecent(HttpContext http, IdentityBffOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        return http.Request.Cookies.ContainsKey(CookieNameOf(options));
    }

    /// <summary>The <c>401</c> problem a held-back sign-in answers.</summary>
    /// <param name="http">The request.</param>
    public static Task WriteAsync(HttpContext http)
        => BffProblemWriter.WriteAsync(http, StatusCodes.Status401Unauthorized, BffErrorCodeConstants.SignInFailed, Detail);

    /// <summary>The <c>401</c> problem result a held-back sign-in answers.</summary>
    public static IResult Result() => BffProblemWriter.Create(StatusCodes.Status401Unauthorized, BffErrorCodeConstants.SignInFailed, Detail);

    private static string CookieNameOf(IdentityBffOptions options)
        => EnvironmentCookieName.For(options.CookieName, options.Environment, options.PrimaryEnvironment) + ".sign-in-failed";
}

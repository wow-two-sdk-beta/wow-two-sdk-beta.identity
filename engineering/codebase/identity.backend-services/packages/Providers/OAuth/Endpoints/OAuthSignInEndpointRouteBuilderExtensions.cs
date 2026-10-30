using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using WoW.Two.Sdk.Beta.Be.Identity.Claims;

namespace WoW.Two.Sdk.Beta.Be.Identity.OAuth.Endpoints;

/// <summary>Maps the browser sign-in surface of an OAuth provider: begin sign-in, read the signed-in user, sign out.</summary>
public static class OAuthSignInEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps, under <paramref name="endpoints"/> (mount it on a group such as <c>app.MapGroup("/api/identity")</c>):
    /// <list type="bullet">
    ///   <item><c>GET sign-in?returnUrl=</c>, anonymous: challenges <paramref name="scheme"/>, returning to
    ///   <c>returnUrl</c> when it is local and to <c>/</c> otherwise; a 503 problem carrying
    ///   <see cref="OAuthSignInEndpointOptions.NotConfiguredDetail"/> while the scheme is not registered, so a host
    ///   registers the provider only when its client id and secret are configured and still boots without them.</item>
    ///   <item><c>GET me</c>, anonymous: the signed-in user as <see cref="OAuthSignedInUserDto"/>, read from the
    ///   normalized claims; a 401 problem for an anonymous caller, so a SPA renders its own sign-in.</item>
    ///   <item><c>POST sign-out</c>, the default authorization policy: signs out of
    ///   <see cref="OAuthSignInEndpointOptions.SignOutScheme"/>, 204.</item>
    /// </list>
    /// The provider's callback path stays its own option (<c>CallbackPath</c>); point it inside the same group.
    /// </summary>
    /// <param name="endpoints">The route builder or group.</param>
    /// <param name="scheme">The OAuth challenge scheme, such as <c>GitHub</c>.</param>
    /// <param name="configure">Optional override of the not-configured detail and the sign-out scheme.</param>
    /// <returns>The same route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapOAuthSignInEndpoints(
        this IEndpointRouteBuilder endpoints,
        string scheme,
        Action<OAuthSignInEndpointOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);

        var options = new OAuthSignInEndpointOptions();
        configure?.Invoke(options);

        endpoints.MapGet("/sign-in", (string? returnUrl, IAuthenticationSchemeProvider schemes) =>
                SignInAsync(scheme, options, returnUrl, schemes))
            .AllowAnonymous();
        endpoints.MapGet("/me", Me).AllowAnonymous();
        endpoints.MapPost("/sign-out", async (HttpContext http) =>
            {
                await http.SignOutAsync(options.SignOutScheme);
                return Results.NoContent();
            })
            .RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> SignInAsync(
        string scheme,
        OAuthSignInEndpointOptions options,
        string? returnUrl,
        IAuthenticationSchemeProvider schemes)
    {
        // 503 until the provider is configured — clearer than a 500 from challenging an unregistered scheme.
        if (await schemes.GetSchemeAsync(scheme) is null)
            return Results.Problem(detail: options.NotConfiguredDetail, statusCode: StatusCodes.Status503ServiceUnavailable);

        // Local redirects only — never bounce the browser to an attacker-supplied absolute URL.
        var target = !string.IsNullOrEmpty(returnUrl) && RedirectHttpResult.IsLocalUrl(returnUrl) ? returnUrl : "/";
        return Results.Challenge(new AuthenticationProperties { RedirectUri = target }, [scheme]);
    }

    private static IResult Me(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);

        return Results.Ok(new OAuthSignedInUserDto
        {
            Login = user.GetUsername() ?? user.FindFirstValue(ClaimTypes.Name) ?? "",
            Name = user.GetDisplayName() ?? user.GetUsername() ?? "",
            Avatar = user.GetAvatar(),
        });
    }
}

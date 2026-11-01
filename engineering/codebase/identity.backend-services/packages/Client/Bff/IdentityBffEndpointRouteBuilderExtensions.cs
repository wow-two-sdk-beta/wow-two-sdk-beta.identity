using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Bff.Antiforgery;
using WoW.Two.Sdk.Beta.Be.Identity.Bff.Endpoints;
using WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff;

/// <summary>Maps the BFF's session routes under <see cref="IdentityBffOptions.RoutePrefix"/>.</summary>
public static class IdentityBffEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Map the session routes under <see cref="IdentityBffOptions.RoutePrefix"/> (default <c>/api/identity</c>):
    ///   - <c>GET /session</c>: <c>{ data: { isAuthenticated, person, organization, permissions, csrfToken, expiresAt } }</c>,
    ///     or <c>{ data: { isAuthenticated: false } }</c> without a session; never cached
    ///   - <c>GET /sign-in?returnUrl=</c>: 302 to the instance's authorize endpoint, back at <c>returnUrl</c> (local paths only,
    ///     else <c>/</c>) once signed in
    ///   - <c>POST /sign-out</c>: with a session, needs <c>X-Csrf-Token</c>, ends it here and answers
    ///     <c>{ data: { endSessionUrl } }</c>, the instance's end-session URL the SPA sends the browser to
    /// The code callback (<c>/callback</c>) is the OpenID Connect handler's own. Map them behind <c>UseIdentityBff</c>.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    public static IEndpointRouteBuilder MapIdentityBffEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<IdentityBffOptions>>().Value;
        var group = endpoints.MapGroup(options.RoutePrefix).AllowAnonymous();
        group.MapGet(BffPathConstants.Session, SessionAsync);
        group.MapGet(BffPathConstants.SignIn, SignIn);
        group.MapPost(BffPathConstants.SignOut, EndSessionAsync);
        return endpoints;
    }

    private static async Task<IResult> SessionAsync(HttpContext http, BffCsrfTokenService csrf)
    {
        http.Response.Headers.CacheControl = "no-store";
        var session = await http.AuthenticateAsync(BffAuthenticationSchemeConstants.Cookie);
        if (!session.Succeeded || BffSessionReference.From(session.Properties) is not { } reference)
            return Results.Ok(ApiResponse<BffSignedOutSessionDto>.Ok(new BffSignedOutSessionDto()));

        var expiresAt = session.Properties!.ExpiresUtc ?? DateTimeOffset.MinValue;
        return Results.Ok(ApiResponse<BffSessionDto>.Ok(BffSessionMapper.ToDto(session.Principal!, csrf.Create(reference.SessionId), expiresAt)));
    }

    private static IResult SignIn(string? returnUrl)
        => Results.Challenge(
            new AuthenticationProperties { RedirectUri = BffReturnUrl.Normalize(returnUrl) },
            [BffAuthenticationSchemeConstants.OpenIdConnect]);

    private static async Task<IResult> EndSessionAsync(
        HttpContext http,
        BffCsrfTokenService csrf,
        BffSessionService sessions,
        IOptions<IdentityBffOptions> options,
        IOptionsMonitor<OpenIdConnectOptions> handlerOptions,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        string? identityToken = null;
        var session = await http.AuthenticateAsync(BffAuthenticationSchemeConstants.Cookie);
        if (session.Succeeded && BffSessionReference.From(session.Properties) is { } reference)
        {
            if (!csrf.IsValid(reference.SessionId, http.Request.Headers[BffHeaderConstants.CsrfToken].ToString()))
            {
                return BffProblemWriter.Create(StatusCodes.Status400BadRequest, BffErrorCodeConstants.AntiforgeryValidationFailed,
                    $"Signing out of a session needs the {BffHeaderConstants.CsrfToken} header from the session route.");
            }

            identityToken = session.Properties!.GetTokenValue("id_token");
            await sessions.EndAsync(reference.SessionId, cancellationToken);
            BffSessionService.MarkEnded(http);
            await http.SignOutAsync(BffAuthenticationSchemeConstants.Cookie);
        }

        var url = await EndSessionUrlAsync(http, options.Value, handlerOptions.Get(BffAuthenticationSchemeConstants.OpenIdConnect), identityToken, cancellationToken);
        return Results.Ok(ApiResponse<BffSignOutDto>.Ok(new BffSignOutDto { EndSessionUrl = url }));
    }

    /// <summary>
    /// The instance's end-session URL: the client, the ID token as <c>id_token_hint</c> while the session had one, and the
    /// post-sign-out URI on this origin. Without discovery it is the post-sign-out URI itself, so the SPA still lands somewhere.
    /// </summary>
    private static async Task<string> EndSessionUrlAsync(
        HttpContext http, IdentityBffOptions options, OpenIdConnectOptions handler, string? identityToken, CancellationToken cancellationToken)
    {
        var request = http.Request;
        var postSignOut = UriHelper.BuildAbsolute(request.Scheme, request.Host, request.PathBase, options.PostSignOutRedirectPath);
        string? endpoint;
        try
        {
            endpoint = (await handler.ConfigurationManager!.GetConfigurationAsync(cancellationToken)).EndSessionEndpoint;
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or IOException)
        {
            endpoint = null;
        }

        if (string.IsNullOrEmpty(endpoint))
            return postSignOut;

        var query = new Dictionary<string, string?> { ["client_id"] = options.ClientId, ["post_logout_redirect_uri"] = postSignOut };
        if (identityToken is not null)
            query["id_token_hint"] = identityToken;
        return QueryHelpers.AddQueryString(endpoint, query);
    }
}

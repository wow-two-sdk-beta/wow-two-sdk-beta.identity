using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Antiforgery;

/// <summary>
/// Validates the antiforgery header of a session's unsafe API requests: a request under an API prefix with a method other
/// than GET, HEAD, OPTIONS or TRACE that carries a live session cookie needs <see cref="BffHeaderConstants.CsrfToken"/>
/// bound to that session, or answers 400 <see cref="BffErrorCodeConstants.AntiforgeryValidationFailed"/>. A request
/// without a session passes on to authorization, which answers 401 where the endpoint needs one. The instance's signal
/// stays out of it: a signed POST no browser sends, and no session to bind to.
/// </summary>
/// <param name="next">The rest of the pipeline.</param>
/// <param name="options">The BFF options.</param>
/// <param name="csrf">The antiforgery tokens.</param>
internal sealed class BffAntiforgeryMiddleware(RequestDelegate next, IOptions<IdentityBffOptions> options, BffCsrfTokenService csrf)
{
    /// <summary>Check the request, then pass it on.</summary>
    /// <param name="http">The request.</param>
    public async Task InvokeAsync(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (BffRequestMatcher.IsUnsafe(http.Request.Method) && BffRequestMatcher.IsApi(http.Request, options.Value) && !BffRequestMatcher.IsSignal(http.Request, options.Value))
        {
            var session = await http.AuthenticateAsync(BffAuthenticationSchemeConstants.Cookie);
            if (session.Succeeded
                && BffSessionReference.From(session.Properties) is { } reference
                && !csrf.IsValid(reference.SessionId, http.Request.Headers[BffHeaderConstants.CsrfToken].ToString()))
            {
                await BffProblemWriter.WriteAsync(http, StatusCodes.Status400BadRequest, BffErrorCodeConstants.AntiforgeryValidationFailed,
                    $"An unsafe request of a session needs the {BffHeaderConstants.CsrfToken} header from the session route.");
                return;
            }
        }

        await next(http);
    }
}

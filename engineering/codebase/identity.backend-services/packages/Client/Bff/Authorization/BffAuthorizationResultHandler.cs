using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Authorization;

/// <summary>
/// Maps a failed route-organization check to a 404 problem, so a session never learns whether another organization's
/// resource exists; every other result goes to <paramref name="inner"/> (the challenge and forbid problems included).
/// </summary>
/// <param name="inner">The result handler registered before the BFF's.</param>
internal sealed class BffAuthorizationResultHandler(IAuthorizationMiddlewareResultHandler inner) : IAuthorizationMiddlewareResultHandler
{
    /// <inheritdoc />
    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);
        if (authorizeResult.Forbidden && authorizeResult.AuthorizationFailure?.FailedRequirements.OfType<RouteOrganizationRequirement>().Any() == true)
            return BffProblemWriter.WriteAsync(context, StatusCodes.Status404NotFound, BffErrorCodeConstants.NotFound, "No such resource in the session's organization.");

        return inner.HandleAsync(next, context, policy, authorizeResult);
    }
}

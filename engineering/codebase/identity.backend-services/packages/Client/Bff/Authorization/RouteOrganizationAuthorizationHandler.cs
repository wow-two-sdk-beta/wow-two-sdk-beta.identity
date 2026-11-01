using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Authorization;

/// <summary>
/// Validates <see cref="RouteOrganizationRequirement"/>: the route value parses as the same organization id as the
/// principal's <c>org_id</c>. A missing or unparsable value fails, as a foreign one does.
/// </summary>
/// <param name="accessor">The request accessor, for a resource that is not the request itself.</param>
internal sealed class RouteOrganizationAuthorizationHandler(IHttpContextAccessor accessor) : AuthorizationHandler<RouteOrganizationRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RouteOrganizationRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);
        var http = context.Resource as HttpContext ?? accessor.HttpContext;
        var routed = Convert.ToString(http?.GetRouteValue(requirement.RouteParameter), CultureInfo.InvariantCulture);
        if (Guid.TryParse(routed, CultureInfo.InvariantCulture, out var organization)
            && Guid.TryParse(context.User.FindFirst(OrganizationClaimTypeConstants.Id)?.Value, CultureInfo.InvariantCulture, out var active)
            && organization == active)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

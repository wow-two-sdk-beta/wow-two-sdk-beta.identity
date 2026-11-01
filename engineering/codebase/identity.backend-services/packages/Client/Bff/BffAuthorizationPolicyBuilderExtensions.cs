using Microsoft.AspNetCore.Authorization;
using WoW.Two.Sdk.Beta.Be.Identity.Bff.Authorization;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff;

/// <summary>Adds the BFF's organization checks to an authorization policy; <c>AddIdentityBff</c> registers their handlers.</summary>
public static class BffAuthorizationPolicyBuilderExtensions
{
    /// <summary>Holds the route parameter <see cref="RequireRouteOrganization"/> reads by default.</summary>
    public const string DefaultOrganizationRouteParameter = "organizationId";

    /// <summary>
    /// Require a signed-in session whose active organization grants <paramref name="permission"/>: an
    /// <c>org_permission</c> value <c>{org_id}:{permission}</c>. A session without it answers 403.
    /// </summary>
    /// <param name="policy">The policy builder.</param>
    /// <param name="permission">The permission, such as <c>desk:edit</c>: the instance's names, the client's namespace included.</param>
    public static AuthorizationPolicyBuilder RequirePermission(this AuthorizationPolicyBuilder policy, string permission)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        return policy.RequireAuthenticatedUser().AddRequirements(new OrganizationPermissionRequirement { Permission = permission });
    }

    /// <summary>
    /// Require a signed-in session whose active organization (<c>org_id</c>) is the one the route names in
    /// <paramref name="routeParameter"/>. A route naming another organization answers 404.
    /// </summary>
    /// <param name="policy">The policy builder.</param>
    /// <param name="routeParameter">The route parameter carrying the organization id. Default <c>organizationId</c>.</param>
    public static AuthorizationPolicyBuilder RequireRouteOrganization(this AuthorizationPolicyBuilder policy, string routeParameter = DefaultOrganizationRouteParameter)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeParameter);
        return policy.RequireAuthenticatedUser().AddRequirements(new RouteOrganizationRequirement { RouteParameter = routeParameter });
    }
}

using Microsoft.AspNetCore.Builder;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff;

/// <summary>Adds the BFF's organization checks to an endpoint or a route group.</summary>
public static class BffEndpointConventionBuilderExtensions
{
    /// <summary>Require <see cref="BffAuthorizationPolicyBuilderExtensions.RequirePermission"/> on the endpoint: 401 without a session, 403 without the permission.</summary>
    /// <typeparam name="TBuilder">The endpoint or group builder.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="permission">The permission, such as <c>desk:edit</c>.</param>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        return builder.RequireAuthorization(policy => policy.RequirePermission(permission));
    }

    /// <summary>Require <see cref="BffAuthorizationPolicyBuilderExtensions.RequireRouteOrganization"/> on the endpoint: 401 without a session, 404 for another organization.</summary>
    /// <typeparam name="TBuilder">The endpoint or group builder.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="routeParameter">The route parameter carrying the organization id. Default <c>organizationId</c>.</param>
    public static TBuilder RequireRouteOrganization<TBuilder>(this TBuilder builder, string routeParameter = BffAuthorizationPolicyBuilderExtensions.DefaultOrganizationRouteParameter)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeParameter);
        return builder.RequireAuthorization(policy => policy.RequireRouteOrganization(routeParameter));
    }
}

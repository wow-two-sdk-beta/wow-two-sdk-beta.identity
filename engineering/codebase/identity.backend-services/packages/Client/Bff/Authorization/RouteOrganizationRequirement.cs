using Microsoft.AspNetCore.Authorization;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Authorization;

/// <summary>Represents the route's organization, which must be the session's active one (<c>org_id</c>); a mismatch answers 404.</summary>
internal sealed record RouteOrganizationRequirement : IAuthorizationRequirement
{
    /// <summary>Gets the route parameter that carries the organization id, such as <c>organizationId</c>.</summary>
    public required string RouteParameter { get; init; }
}

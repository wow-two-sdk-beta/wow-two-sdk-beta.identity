using Microsoft.AspNetCore.Authorization;

namespace WoW.Two.Sdk.Beta.Be.Identity.Authorization;

/// <summary>Builds the authorization policy that asks Identity about a token on the request.</summary>
public static class LiveTokenAuthorizationPolicyBuilderExtensions
{
    /// <summary>
    /// Requires an authenticated caller whose bearer token Identity confirms over introspection on this request, for named dangerous
    /// actions only: every other request stays offline, and this one costs a round trip with nothing cached.
    /// </summary>
    /// <remarks>
    /// Needs <c>AddLiveTokenCheck</c>. An inactive token or an unreachable endpoint is forbidden (fails closed); an anonymous caller is
    /// challenged.
    /// </remarks>
    /// <param name="builder">The policy builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static AuthorizationPolicyBuilder RequireLiveToken(this AuthorizationPolicyBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.RequireAuthenticatedUser().AddRequirements(LiveTokenRequirement.Instance);
    }
}

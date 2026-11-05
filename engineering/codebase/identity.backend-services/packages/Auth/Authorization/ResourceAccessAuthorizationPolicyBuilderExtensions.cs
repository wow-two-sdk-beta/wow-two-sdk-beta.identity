using Microsoft.AspNetCore.Authorization;

namespace WoW.Two.Sdk.Beta.Be.Identity.Authorization;

/// <summary>Builds authorization policies over a caller's scopes and audiences, whichever scheme authenticated it.</summary>
public static class ResourceAccessAuthorizationPolicyBuilderExtensions
{
    /// <summary>Requires an authenticated caller that holds <paramref name="scope"/>; repeated calls all apply.</summary>
    /// <remarks>A caller without the scope is forbidden; an anonymous one is challenged.</remarks>
    /// <param name="builder">The policy builder.</param>
    /// <param name="scope">The scope the caller must hold.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static AuthorizationPolicyBuilder RequireScope(this AuthorizationPolicyBuilder builder, string scope)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        return builder.RequireAuthenticatedUser().RequireAssertion(context => context.User.HasScope(scope));
    }

    /// <summary>
    /// Requires an authenticated caller whose token names <paramref name="audience"/>, for an endpoint narrower than the host.
    /// </summary>
    /// <remarks>A caller without the audience is forbidden; an anonymous one is challenged.</remarks>
    /// <param name="builder">The policy builder.</param>
    /// <param name="audience">The audience the token must name.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static AuthorizationPolicyBuilder RequireAudience(this AuthorizationPolicyBuilder builder, string audience)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        return builder.RequireAuthenticatedUser().RequireAssertion(context => context.User.HasAudience(audience));
    }
}

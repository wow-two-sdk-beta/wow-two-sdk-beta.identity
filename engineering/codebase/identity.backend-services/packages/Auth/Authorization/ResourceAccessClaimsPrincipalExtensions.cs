using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core;

namespace WoW.Two.Sdk.Beta.Be.Identity.Authorization;

/// <summary>Reads what a caller may reach on a resource: the scopes it holds and the audiences it names.</summary>
public static class ResourceAccessClaimsPrincipalExtensions
{
    /// <summary>The name some issuers use for <see cref="ServiceClaimTypeConstants.Scope"/>.</summary>
    private const string ScpClaimType = "scp";

    /// <summary>
    /// Tells whether the principal holds a scope, from <c>scope</c> or <c>scp</c> claims: one space-delimited JWT claim and
    /// one claim per scope both work.
    /// </summary>
    /// <param name="principal">The principal to read.</param>
    /// <param name="scope">The scope to look for, compared ordinally.</param>
    /// <returns><c>true</c> when the principal holds the scope.</returns>
    public static bool HasScope(this ClaimsPrincipal principal, string scope)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        return principal.Claims
            .Where(claim => claim.Type is ServiceClaimTypeConstants.Scope or ScpClaimType)
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);
    }

    /// <summary>Tells whether the principal's token names an audience.</summary>
    /// <param name="principal">The principal to read.</param>
    /// <param name="audience">The audience to look for, compared ordinally.</param>
    /// <returns><c>true</c> when an <c>aud</c> claim equals the audience.</returns>
    public static bool HasAudience(this ClaimsPrincipal principal, string audience)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        return principal.Claims.Any(claim =>
            claim.Type == JwtRegisteredClaimNames.Aud && string.Equals(claim.Value, audience, StringComparison.Ordinal));
    }
}

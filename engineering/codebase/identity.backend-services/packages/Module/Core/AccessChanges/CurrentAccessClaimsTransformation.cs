using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.AccessChanges;

/// <summary>
/// Keeps a cookie's roles and permissions current: a role change signs nobody out, so each request's principal is rebuilt from
/// the store, keeping only what the sign-in itself decided (<c>sid</c> and <c>amr</c>). Only the sign-in cookie is touched; a
/// token says what it said when it was issued, and a person with no account left is the cookie checks' to refuse.
/// </summary>
/// <param name="options">Identity options carrying the claim types.</param>
/// <param name="accounts">The account service.</param>
/// <param name="factory">Builds the current claims.</param>
internal sealed class CurrentAccessClaimsTransformation(IdentityCoreOptions options, UserAccountService accounts, UserClaimsPrincipalFactory factory) : IClaimsTransformation
{
    /// <inheritdoc />
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var types = options.Claims;
        if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true, AuthenticationType: UserClaimsPrincipalFactory.DefaultAuthenticationType or CookieAuthenticationDefaults.AuthenticationScheme } cookie
            || !Guid.TryParse(cookie.FindFirst(types.UserIdClaimType)?.Value, out var userId)
            || await accounts.FindByIdAsync(userId) is not { } user)
            return principal;

        var current = await factory.CreateAsync(user, cookie.AuthenticationType!);
        var identity = (ClaimsIdentity)current.Identity!;
        identity.AddClaims(cookie.Claims.Where(claim => claim.Type == types.SessionIdClaimType || claim.Type == AuthenticationMethodConstants.ClaimType));
        return current;
    }
}

using OpenIddict.EntityFrameworkCore.Models;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>
/// One token the OpenID Connect server issued (<c>identity_oidc_tokens</c>): an authorization code, an access, identity
/// or refresh token, with its status, so a code or refresh token redeems once and a revoked one stops working.
/// </summary>
/// <remarks>The id is a uuid v7 made in code; <c>subject</c> holds the user id (<c>sub</c>) as text.</remarks>
public sealed class IdentityOidcToken : OpenIddictEntityFrameworkCoreToken<Guid, IdentityOidcApplication, IdentityOidcAuthorization>
{
    /// <summary>Create a token with a fresh uuid v7 id.</summary>
    public IdentityOidcToken() => Id = Guid.CreateVersion7();
}

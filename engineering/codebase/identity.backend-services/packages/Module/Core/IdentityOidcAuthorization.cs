using OpenIddict.EntityFrameworkCore.Models;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>
/// A grant of scopes from one subject to one client (<c>identity_oidc_authorizations</c>); OpenIddict creates an ad-hoc
/// one per code flow and ties the flow's tokens to it, so revoking it ends them all.
/// </summary>
/// <remarks>The id is a uuid v7 made in code; <c>subject</c> holds the user id (<c>sub</c>) as text.</remarks>
public sealed class IdentityOidcAuthorization : OpenIddictEntityFrameworkCoreAuthorization<Guid, IdentityOidcApplication, IdentityOidcToken>
{
    /// <summary>Create an authorization with a fresh uuid v7 id.</summary>
    public IdentityOidcAuthorization() => Id = Guid.CreateVersion7();
}

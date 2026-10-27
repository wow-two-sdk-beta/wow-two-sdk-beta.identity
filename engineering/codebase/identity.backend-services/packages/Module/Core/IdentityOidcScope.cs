using OpenIddict.EntityFrameworkCore.Models;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>A scope the OpenID Connect server knows beyond the standard ones, with its resources (<c>identity_oidc_scopes</c>).</summary>
/// <remarks>The id is a uuid v7 made in code. Empty until a custom scope is registered.</remarks>
public sealed class IdentityOidcScope : OpenIddictEntityFrameworkCoreScope<Guid>
{
    /// <summary>Create a scope with a fresh uuid v7 id.</summary>
    public IdentityOidcScope() => Id = Guid.CreateVersion7();
}

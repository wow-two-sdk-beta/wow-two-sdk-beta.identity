using OpenIddict.EntityFrameworkCore.Models;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>
/// A registered OpenID Connect client (<c>identity_oidc_applications</c>): its client id, hashed secret, redirect URIs and
/// permissions. OpenIddict's EF Core stores read and write it through <see cref="IdentityDbContext"/>; the Server package
/// seeds it from configuration.
/// </summary>
/// <remarks>The id is a uuid v7 made in code, as for every identity table.</remarks>
public sealed class IdentityOidcApplication : OpenIddictEntityFrameworkCoreApplication<Guid, IdentityOidcAuthorization, IdentityOidcToken>
{
    /// <summary>Create an application with a fresh uuid v7 id.</summary>
    public IdentityOidcApplication() => Id = Guid.CreateVersion7();
}

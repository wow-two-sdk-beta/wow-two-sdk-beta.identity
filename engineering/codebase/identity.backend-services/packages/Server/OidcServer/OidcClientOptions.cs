namespace WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

/// <summary>
/// One registered client: a confidential, first-party app that signs people in with the code flow and PKCE, calls APIs as
/// itself, or both (<see cref="Grants"/>). Seeded into the client registry at startup; the secret is stored as OpenIddict's
/// hash only.
/// </summary>
public sealed record OidcClientOptions
{
    /// <summary>Gets or sets the client id, such as <c>wheelhouse</c>; unique.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Gets or sets the client secret the app authenticates with at the token endpoint; the owner generates it. Set this or <see cref="JsonWebKeySet"/>.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the JSON Web Key Set, public keys only, whose keys sign the app's <c>private_key_jwt</c> assertions at the
    /// token endpoint: set instead of <see cref="ClientSecret"/>. A BFF signs people in with its secret, so this suits a client
    /// that only calls APIs.
    /// </summary>
    public string? JsonWebKeySet { get; set; }

    /// <summary>Gets or sets the display name; null uses the client id.</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets what the client may do, from <see cref="OidcClientGrantConstants"/>; empty means
    /// <see cref="OidcClientGrantConstants.AuthorizationCode"/> alone.
    /// </summary>
    public List<string> Grants { get; set; } = [];

    /// <summary>Gets or sets the exact redirect URIs the app receives codes at; HTTPS, or HTTP on a loopback host.</summary>
    public List<string> RedirectUris { get; set; } = [];

    /// <summary>Gets or sets the exact URIs end-session may return to; HTTPS, or HTTP on a loopback host.</summary>
    public List<string> PostSignOutRedirectUris { get; set; } = [];

    /// <summary>Gets or sets the audience (<c>aud</c>) of the app's access tokens; null uses the client id. The app accepts only its own.</summary>
    public string? Audience { get; set; }

    /// <summary>
    /// Gets or sets the app's permission namespace, such as <c>wheelhouse</c>: its tokens carry only the
    /// <c>org_permission</c> values that start with <c>{namespace}:</c>. Null carries every permission of the role.
    /// </summary>
    public string? PermissionNamespace { get; set; }

    /// <summary>Gets or sets the names of the resources it may call as itself (<see cref="OidcClientGrantConstants.ClientCredentials"/>).</summary>
    public List<string> Resources { get; set; } = [];

    /// <summary>Gets or sets the names of the resources it may call for a person (<see cref="OidcClientGrantConstants.TokenExchange"/>).</summary>
    public List<string> ExchangeResources { get; set; } = [];

    /// <summary>Gets or sets the scopes it may ask for on those resources; a token holds only those its own resource understands.</summary>
    public List<string> Scopes { get; set; } = [];

    /// <summary>Gets or sets where it hears revocation signals; null hears none. HTTPS unless <c>Identity:Server:Signals:RequireHttps</c> is off.</summary>
    public string? SignalUrl { get; set; }

    /// <summary>Gets or sets whether it may ask <c>/connect/introspect</c> if a token stands now, for a dangerous action.</summary>
    public bool CanIntrospect { get; set; }

    /// <summary>Gets or sets the organization its own tokens carry as <c>org_id</c>, for a client bound to one; null carries none.</summary>
    public Guid? OrganizationId { get; set; }
}

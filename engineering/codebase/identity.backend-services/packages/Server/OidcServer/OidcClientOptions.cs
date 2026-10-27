namespace WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

/// <summary>
/// One registered client: a confidential, first-party app that signs people in with the code flow and PKCE. Seeded into
/// the client registry at startup; the secret is stored as OpenIddict's hash only.
/// </summary>
public sealed record OidcClientOptions
{
    /// <summary>Gets or sets the client id, such as <c>wheelhouse</c>; unique.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Gets or sets the client secret the app authenticates with at the token endpoint; the owner generates it.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Gets or sets the display name; null uses the client id.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Gets or sets the exact redirect URIs the app receives codes at; HTTPS, or HTTP on a loopback host.</summary>
    public List<string> RedirectUris { get; set; } = [];

    /// <summary>Gets or sets the exact URIs end-session may return to; HTTPS, or HTTP on a loopback host.</summary>
    public List<string> PostLogoutRedirectUris { get; set; } = [];

    /// <summary>Gets or sets the audience (<c>aud</c>) of the app's access tokens; null uses the client id. The app accepts only its own.</summary>
    public string? Audience { get; set; }

    /// <summary>
    /// Gets or sets the app's permission namespace, such as <c>wheelhouse</c>: its tokens carry only the
    /// <c>org_permission</c> values that start with <c>{namespace}:</c>. Null carries every permission of the role.
    /// </summary>
    public string? PermissionNamespace { get; set; }
}

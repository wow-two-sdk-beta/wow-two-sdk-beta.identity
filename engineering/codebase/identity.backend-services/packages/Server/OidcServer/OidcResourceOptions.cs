namespace WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

/// <summary>
/// One API a service token may be issued for: a service, or one instance of it, with its audience and the scopes it
/// understands. Clients name it from <see cref="OidcClientOptions.Resources"/> and <see cref="OidcClientOptions.ExchangeResources"/>.
/// </summary>
public sealed record OidcResourceOptions
{
    /// <summary>Gets or sets the name clients refer to it by, such as <c>files</c>; unique, and the same across environments.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resource URI, such as <c>https://files.example.com/</c>: what a caller sends as <c>resource</c> and
    /// the only <c>aud</c> of its tokens. Written as <see cref="Uri.AbsoluteUri"/>, trailing slash included, since the
    /// token endpoint compares it exactly; HTTPS, or HTTP on a loopback host.
    /// </summary>
    public string Uri { get; set; } = string.Empty;

    /// <summary>Gets or sets the scopes it understands, each <c>{service}.{action}</c>, such as <c>files.read</c>.</summary>
    public List<string> Scopes { get; set; } = [];

    /// <summary>
    /// Gets or sets the permission namespace of a token exchanged for it, such as <c>files</c>: the token carries only the
    /// person's <c>org_permission</c> values that start with <c>{namespace}:</c>. Null carries every permission of the role.
    /// </summary>
    public string? PermissionNamespace { get; set; }

    /// <summary>Gets or sets where it hears revocation signals; null hears none. HTTPS unless <c>Identity:Server:Signals:RequireHttps</c> is off.</summary>
    public string? SignalUrl { get; set; }
}

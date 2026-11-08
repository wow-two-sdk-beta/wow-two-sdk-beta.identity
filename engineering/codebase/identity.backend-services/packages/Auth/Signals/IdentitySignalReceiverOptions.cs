using WoW.Two.Sdk.Beta.Be.Identity.Jwt;

namespace WoW.Two.Sdk.Beta.Be.Identity.Signals;

/// <summary>Holds who a service hears revocation signals from, and the URL a signal must name.</summary>
public sealed record IdentitySignalReceiverOptions
{
    /// <summary>
    /// Gets or sets this service's own signal URL, as registered at the issuer: the only <c>aud</c> a signature may name, so a
    /// signal captured on its way to another service can't be replayed here.
    /// </summary>
    public string Audience { get; set; } = "";

    /// <summary>Gets or sets the signal URLs accepted beside <see cref="Audience"/>: a host that is a resource and a BFF has one for each.</summary>
    public List<string> Audiences { get; set; } = [];

    /// <summary>
    /// Gets or sets the issuers signals are accepted from, beside those the host's <see cref="ITrustedIssuerResolver"/> answers when
    /// one is registered (as <see cref="JwtOptions.UseTrustedIssuerResolver{TResolver}"/> does). Each keeps its key source; its
    /// <see cref="TrustedIssuer.Audiences"/> and <see cref="TrustedIssuer.Partition"/> describe its tokens and are ignored for a signal.
    /// </summary>
    public List<TrustedIssuer> TrustedIssuers { get; set; } = [];

    /// <summary>Gets or sets whether HTTP metadata is allowed for an explicit local-development setup. Default <c>false</c>.</summary>
    public bool IsInsecureMetadataAllowedInDevelopment { get; set; }

    /// <summary>Gets or sets the handler discovery requests go through, for a test host; null uses the network.</summary>
    public HttpMessageHandler? BackchannelHttpHandler { get; set; }

    /// <summary>Gets the signal URLs in the form a sender signs them, <see cref="Uri.AbsoluteUri"/>; the boot checked they parse.</summary>
    internal string[] AcceptedAudiences =>
        (string.IsNullOrWhiteSpace(Audience) ? Audiences : Audiences.Prepend(Audience))
            .Select(audience => new Uri(audience, UriKind.Absolute).AbsoluteUri)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>Gets whether this host names a signal URL.</summary>
    internal bool HasSignalUrl => !string.IsNullOrWhiteSpace(Audience) || Audiences.Count > 0;

    /// <summary>Gets whether this host names a signal URL and an issuer, listed or resolved, the least a mapped route needs.</summary>
    /// <param name="hasResolver">Whether the host registered an <see cref="ITrustedIssuerResolver"/>.</param>
    internal bool IsConfiguredWith(bool hasResolver) => HasSignalUrl && (TrustedIssuers.Count > 0 || hasResolver);
}

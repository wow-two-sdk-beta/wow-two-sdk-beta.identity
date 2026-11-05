using Microsoft.IdentityModel.Tokens;

namespace WoW.Two.Sdk.Beta.Be.Identity.Jwt;

/// <summary>Holds jWT bearer registration options.</summary>
public sealed record JwtOptions
{
    /// <summary>Required token issuer with one issuer; trusted-issuer mode refuses it.</summary>
    public string Issuer { get; set; } = "";

    /// <summary>Required token audience.</summary>
    public string Audience { get; set; } = "";

    /// <summary>
    /// Gets or sets the audiences accepted beside <see cref="Audience"/>, in both modes: a service often accepts its own
    /// person tokens and its resource URI.
    /// </summary>
    public List<string> Audiences { get; set; } = [];

    /// <summary>
    /// Gets or sets the issuers whose tokens are accepted. One entry turns trusted-issuer mode on, which refuses
    /// <see cref="Issuer"/>, <see cref="SymmetricKey"/> and <see cref="MetadataAddress"/>.
    /// </summary>
    public List<TrustedIssuer> TrustedIssuers { get; set; } = [];

    /// <summary>Gets or sets the symmetric verification key. Configure exactly one of this property and <see cref="MetadataAddress"/>.</summary>
    public string? SymmetricKey { get; set; }

    /// <summary>Gets or sets the OpenID Connect discovery metadata address. Configure exactly one of this property and <see cref="SymmetricKey"/>.</summary>
    public Uri? MetadataAddress { get; set; }

    /// <summary>
    /// Gets or sets the only accepted JWT signing algorithm with one issuer. Default <c>HS256</c>. Trusted-issuer mode
    /// ignores it and accepts the asymmetric family only.
    /// </summary>
    public string Algorithm { get; set; } = SecurityAlgorithms.HmacSha256;

    /// <summary>Gets or sets whether HTTP metadata is allowed for an explicit local-development setup. Default <c>false</c>.</summary>
    public bool IsInsecureMetadataAllowedInDevelopment { get; set; }

    /// <summary>Validate token expiration. Default <c>true</c>.</summary>
    public bool IsLifetimeValidated { get; set; } = true;

    /// <summary>Clock skew. Default 30 seconds.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the handler discovery requests go through, for a test host; null uses the network.</summary>
    public HttpMessageHandler? BackchannelHttpHandler { get; set; }

    /// <summary>Gets the resolver recorded by <see cref="UseTrustedIssuerResolver{TResolver}"/>.</summary>
    internal Type? TrustedIssuerResolverType { get; private set; }

    /// <summary>Gets whether tokens are checked against <see cref="TrustedIssuers"/> and a resolver, not one issuer.</summary>
    internal bool IsTrustedIssuerMode => TrustedIssuers.Count > 0 || TrustedIssuerResolverType is not null;

    /// <summary>
    /// Asks <typeparamref name="TResolver"/>, registered scoped, about a token whose issuer <see cref="TrustedIssuers"/>
    /// doesn't list. Turns trusted-issuer mode on.
    /// </summary>
    /// <typeparam name="TResolver">The resolver.</typeparam>
    public void UseTrustedIssuerResolver<TResolver>()
        where TResolver : class, ITrustedIssuerResolver =>
        TrustedIssuerResolverType = typeof(TResolver);
}

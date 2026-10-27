using WoW.Two.Sdk.Beta.Be.Identity.Keys;

namespace WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

/// <summary>Holds the OpenID Connect server's settings: issuer, sign-in path, token lifetimes, keys and the client registry.</summary>
/// <remarks>Set in code with <c>.AddOidcServer(o => …)</c> or in the host section <c>Identity:Server</c>, which is applied last.</remarks>
public sealed record OidcServerOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Identity:Server";

    /// <summary>Gets or sets the issuer, such as <c>https://identity.example.com/</c>; null derives it from each request's host.</summary>
    /// <remarks>Set it on a deployed instance, so tokens never carry a host a proxy or a probe chose.</remarks>
    public Uri? Issuer { get; set; }

    /// <summary>Gets or sets where an authorization request without a session sends the person, with <c>returnUrl</c> appended. Default <c>/sign-in</c>.</summary>
    public string SignInPath { get; set; } = "/sign-in";

    /// <summary>Gets or sets how long an authorization code stays redeemable. Default 60 seconds.</summary>
    public TimeSpan AuthorizationCodeLifetime { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Gets or sets how long an access token stays valid. Default 10 minutes.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Gets or sets how long an ID token stays valid. Default 5 minutes.</summary>
    public TimeSpan IdentityTokenLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets or sets the absolute lifetime of a refresh token chain, counted from the sign-in; each use rotates the token without extending it. Default 12 hours.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Gets or sets where the signing and encryption certificates come from (<c>Identity:Server:Keys</c>).</summary>
    public ServerKeyOptions Keys { get; set; } = new();

    /// <summary>Gets or sets the registered clients, seeded at startup (<c>Identity:Server:Clients</c>).</summary>
    public List<OidcClientOptions> Clients { get; set; } = [];
}

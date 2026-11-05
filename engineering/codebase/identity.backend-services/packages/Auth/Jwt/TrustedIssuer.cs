using Microsoft.IdentityModel.Tokens;

namespace WoW.Two.Sdk.Beta.Be.Identity.Jwt;

/// <summary>Holds one issuer a resource accepts tokens from, and what those tokens are checked against.</summary>
public sealed record TrustedIssuer
{
    /// <summary>Gets the issuer, compared exactly with a token's <c>iss</c>.</summary>
    public required string Issuer { get; init; }

    /// <summary>Gets the discovery address; null means <c>{Issuer trimmed}/.well-known/openid-configuration</c>.</summary>
    public Uri? MetadataAddress { get; init; }

    /// <summary>Gets static signing keys used instead of discovery: a fake issuer, a test host.</summary>
    public IReadOnlyList<SecurityKey>? SigningKeys { get; init; }

    /// <summary>Gets the audiences this issuer's tokens may name; empty means those of <see cref="JwtOptions"/>.</summary>
    public IReadOnlyList<string> Audiences { get; init; } = [];

    /// <summary>Gets the tenant stamped on the principal as the <c>partition</c> claim; null stamps none.</summary>
    public string? Partition { get; init; }
}

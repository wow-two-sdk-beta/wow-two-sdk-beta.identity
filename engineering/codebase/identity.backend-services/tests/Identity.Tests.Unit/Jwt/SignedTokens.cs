using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Jwt;

/// <summary>Mints tokens by hand, for what no honest issuer signs: another issuer's name, forged claims, the wrong algorithm.</summary>
internal static class SignedTokens
{
    private static readonly JsonWebTokenHandler Handler = new();

    /// <summary>Creates a throwaway RSA signing key.</summary>
    /// <param name="keyId">The key's <c>kid</c>; null generates one.</param>
    public static RsaSecurityKey NewKey(string? keyId = null) =>
        new(RSA.Create(2048)) { KeyId = keyId ?? Guid.NewGuid().ToString("N") };

    /// <summary>The public half of a key, as a verifier holds it.</summary>
    /// <param name="key">The signing key.</param>
    public static RsaSecurityKey PublicOf(RsaSecurityKey key) =>
        new(key.Rsa.ExportParameters(includePrivateParameters: false)) { KeyId = key.KeyId };

    /// <summary>Signs a token with RS256, or with <paramref name="algorithm"/>.</summary>
    /// <param name="key">The signing key.</param>
    /// <param name="issuer">The <c>iss</c>.</param>
    /// <param name="audience">The <c>aud</c>.</param>
    /// <param name="claims">More claims.</param>
    /// <param name="algorithm">The algorithm.</param>
    public static string Sign(
        SecurityKey key,
        string issuer,
        string audience,
        IDictionary<string, object>? claims = null,
        string algorithm = SecurityAlgorithms.RsaSha256) =>
        Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(key, algorithm),
            Claims = claims ?? new Dictionary<string, object> { [JwtRegisteredClaimNames.Sub] = "caller" },
        });
}

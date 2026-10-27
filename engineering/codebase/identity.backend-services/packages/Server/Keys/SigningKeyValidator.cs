using System.Globalization;
using System.Security.Cryptography.X509Certificates;

namespace WoW.Two.Sdk.Beta.Be.Identity.Keys;

/// <summary>
/// Checks a <see cref="SigningKeySet"/> before the server uses it: each role holds a certificate, each certificate has
/// an RSA private key of 2048 bits or more and the key usage of its role, and each role has one certificate valid now.
/// </summary>
/// <remarks>An expired or staged certificate beside a valid one passes: it verifies or decrypts older tokens, or takes over by date.</remarks>
public static class SigningKeyValidator
{
    /// <summary>The smallest RSA key accepted, in bits.</summary>
    public const int MinimumKeySize = 2048;

    /// <summary>The problems that make <paramref name="keys"/> unusable at <paramref name="now"/>; empty when it is usable.</summary>
    /// <param name="keys">The certificates.</param>
    /// <param name="now">The instant to judge validity at.</param>
    public static IReadOnlyList<string> Validate(SigningKeySet keys, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var problems = new List<string>();
        foreach (var role in Enum.GetValues<SigningKeyRole>())
        {
            var certificates = keys.Of(role);
            if (certificates.Count == 0)
            {
                problems.Add($"No {Name(role)} certificate is configured (Identity:Server:Keys:{role}).");
                continue;
            }

            foreach (var certificate in certificates)
                problems.AddRange(CheckCertificate(role, certificate));

            if (SigningKeySet.SelectActive(certificates, now) is null)
                problems.Add($"No {Name(role)} certificate is valid at {now.ToString("u", CultureInfo.InvariantCulture)}: {string.Join(", ", certificates.Select(Describe))}.");
        }

        return problems;
    }

    /// <summary>A certificate's thumbprint and validity dates, for messages and logs.</summary>
    /// <param name="certificate">The certificate.</param>
    public static string Describe(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return string.Create(CultureInfo.InvariantCulture, $"{certificate.Thumbprint} ({certificate.NotBefore.ToUniversalTime():u} to {certificate.NotAfter.ToUniversalTime():u})");
    }

    private static IEnumerable<string> CheckCertificate(SigningKeyRole role, X509Certificate2 certificate)
    {
        if (!certificate.HasPrivateKey)
        {
            yield return $"The {Name(role)} certificate {Describe(certificate)} has no private key.";
            yield break;
        }

        using var rsa = certificate.GetRSAPrivateKey();
        if (rsa is null)
            yield return $"The {Name(role)} certificate {Describe(certificate)} is not RSA; the server advertises RS256 and RSA-OAEP.";
        else if (rsa.KeySize < MinimumKeySize)
            yield return $"The {Name(role)} certificate {Describe(certificate)} has a {rsa.KeySize}-bit key; {MinimumKeySize} bits is the floor.";

        var usage = role == SigningKeyRole.Signing ? X509KeyUsageFlags.DigitalSignature : X509KeyUsageFlags.KeyEncipherment;
        if (certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault() is { } extension && !extension.KeyUsages.HasFlag(usage))
            yield return $"The {Name(role)} certificate {Describe(certificate)} lacks the {usage} key usage.";
    }

    private static string Name(SigningKeyRole role) => role == SigningKeyRole.Signing ? "signing" : "encryption";
}

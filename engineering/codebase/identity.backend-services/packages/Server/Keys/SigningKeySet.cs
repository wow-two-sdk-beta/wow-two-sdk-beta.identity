using System.Security.Cryptography.X509Certificates;

namespace WoW.Two.Sdk.Beta.Be.Identity.Keys;

/// <summary>Holds the OIDC server's certificates by role, several per role during a rotation, and picks the active one by date.</summary>
/// <param name="Signing">The signing certificates.</param>
/// <param name="Encryption">The encryption certificates.</param>
public sealed record SigningKeySet(IReadOnlyList<X509Certificate2> Signing, IReadOnlyList<X509Certificate2> Encryption)
{
    /// <summary>The certificates of <paramref name="role"/>.</summary>
    /// <param name="role">The role.</param>
    public IReadOnlyList<X509Certificate2> Of(SigningKeyRole role) => role == SigningKeyRole.Signing ? Signing : Encryption;

    /// <summary>
    /// The certificate that signs or encrypts at <paramref name="now"/>: of those valid then, the one expiring last, the
    /// latest start breaking a tie; null when none is valid. A staged certificate (start in the future) never wins early.
    /// </summary>
    /// <param name="certificates">The candidates.</param>
    /// <param name="now">The instant to judge validity at.</param>
    public static X509Certificate2? SelectActive(IEnumerable<X509Certificate2> certificates, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(certificates);
        return certificates
            .Where(certificate => IsValidAt(certificate, now))
            .OrderByDescending(certificate => certificate.NotAfter)
            .ThenByDescending(certificate => certificate.NotBefore)
            .FirstOrDefault();
    }

    /// <summary>Whether <paramref name="certificate"/> is within its validity dates at <paramref name="now"/>.</summary>
    /// <param name="certificate">The certificate.</param>
    /// <param name="now">The instant.</param>
    public static bool IsValidAt(X509Certificate2 certificate, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        // NotBefore and NotAfter are local times; DateTimeOffset carries their offset into the comparison.
        return new DateTimeOffset(certificate.NotBefore) <= now && now <= new DateTimeOffset(certificate.NotAfter);
    }
}

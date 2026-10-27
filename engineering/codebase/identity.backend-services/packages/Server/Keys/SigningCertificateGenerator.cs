using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace WoW.Two.Sdk.Beta.Be.Identity.Keys;

/// <summary>
/// Makes the self-signed certificates the OIDC server signs and encrypts with: RSA 3072, the key usage of the role, and
/// validity dates that drive rotation. The owner runs it through the service's <c>keys generate</c> command; agents and
/// CI never do, outside tests.
/// </summary>
public static class SigningCertificateGenerator
{
    /// <summary>The RSA key size generated, in bits.</summary>
    public const int KeySize = 3072;

    /// <summary>The default validity of a signing certificate: the 90-day rotation cadence plus 30 days of slack.</summary>
    public static readonly TimeSpan DefaultSigningLifetime = TimeSpan.FromDays(120);

    /// <summary>The default validity of an encryption certificate: the 180-day rotation cadence plus 30 days of slack.</summary>
    public static readonly TimeSpan DefaultEncryptionLifetime = TimeSpan.FromDays(210);

    /// <summary>The default validity of a certificate of <paramref name="role"/>.</summary>
    /// <param name="role">The role.</param>
    public static TimeSpan DefaultLifetime(SigningKeyRole role)
        => role == SigningKeyRole.Signing ? DefaultSigningLifetime : DefaultEncryptionLifetime;

    /// <summary>Create a certificate for <paramref name="role"/> valid from <paramref name="notBefore"/> for <paramref name="lifetime"/>.</summary>
    /// <param name="role">The role, which sets the key usage: digital signature or key encipherment.</param>
    /// <param name="notBefore">The start of validity; a future date stages the certificate.</param>
    /// <param name="lifetime">How long it stays valid; positive.</param>
    public static X509Certificate2 Create(SigningKeyRole role, DateTimeOffset notBefore, TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "The lifetime must be positive.");

        using var rsa = RSA.Create(KeySize);
        var subject = new X500DistinguishedName(role == SigningKeyRole.Signing ? "CN=Identity token signing" : "CN=Identity token encryption");
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            role == SigningKeyRole.Signing ? X509KeyUsageFlags.DigitalSignature : X509KeyUsageFlags.KeyEncipherment,
            critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        // Whole seconds: X.509 dates carry no fractions, so the returned dates read back exactly from the PEM.
        var start = DateTimeOffset.FromUnixTimeSeconds(notBefore.ToUnixTimeSeconds());
        return request.CreateSelfSigned(start, start + lifetime);
    }

    /// <summary>The PEM bundle a host file holds: the certificate, then its unencrypted PKCS#8 private key.</summary>
    /// <param name="certificate">A certificate with its RSA private key.</param>
    /// <exception cref="InvalidOperationException">The certificate carries no RSA private key.</exception>
    public static string ExportPem(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        using var rsa = certificate.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("The certificate carries no RSA private key.");

        return new StringBuilder()
            .AppendLine(certificate.ExportCertificatePem())
            .AppendLine(rsa.ExportPkcs8PrivateKeyPem())
            .ToString();
    }
}

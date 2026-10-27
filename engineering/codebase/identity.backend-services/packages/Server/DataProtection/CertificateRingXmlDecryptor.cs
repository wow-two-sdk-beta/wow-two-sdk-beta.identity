using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;

namespace WoW.Two.Sdk.Beta.Be.Identity.DataProtection;

/// <summary>
/// Unwraps a ring key that <c>CertificateRingXmlEncryptor</c> wrapped: it matches the certificate the encrypted key names
/// against the ring certificates in the settings (<see cref="DataProtectionRingOptions.Certificates"/>) and decrypts
/// with that one's private key. The key manager creates it by its type name, which every wrapped key records.
/// </summary>
/// <remarks>No certificate store is consulted: a key wrapped to a certificate no longer listed stays unreadable, and the key manager skips it.</remarks>
public sealed class CertificateRingXmlDecryptor : IXmlDecryptor
{
    private readonly IServiceProvider _services;

    /// <summary>Create the decryptor; the key manager passes its service provider.</summary>
    /// <param name="services">The application's service provider, holding the ring certificates.</param>
    public CertificateRingXmlDecryptor(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <inheritdoc />
    /// <exception cref="System.Security.Cryptography.CryptographicException">No listed certificate unwraps the key.</exception>
    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);

        // EncryptedXml decrypts in place inside a document, so the element sits under a throwaway root.
        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        using (var reader = new XElement("root", encryptedElement).CreateReader())
            document.Load(reader);

        new RingEncryptedXml(document, _services.GetRequiredService<DataProtectionRingCertificates>().All).DecryptDocument();
        using var decrypted = new XmlNodeReader(document.DocumentElement!.FirstChild!);
        return XElement.Load(decrypted);
    }

    /// <summary>XML decryption that finds the key-encryption certificate among the ring's, by the certificate the encrypted key carries.</summary>
    private sealed class RingEncryptedXml(XmlDocument document, IReadOnlyList<X509Certificate2> certificates) : EncryptedXml(document)
    {
        /// <inheritdoc />
        public override byte[]? DecryptEncryptedKey(EncryptedKey encryptedKey)
        {
            ArgumentNullException.ThrowIfNull(encryptedKey);
            var presented = encryptedKey.KeyInfo.OfType<KeyInfoX509Data>()
                .SelectMany(data => data.Certificates?.OfType<X509Certificate2>() ?? [])
                .Select(certificate => certificate.Thumbprint);
            foreach (var thumbprint in presented)
            {
                var match = certificates.FirstOrDefault(certificate => string.Equals(certificate.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase));
                using var rsa = match?.GetRSAPrivateKey();
                if (rsa is not null)
                    return DecryptKey(encryptedKey.CipherData.CipherValue!, rsa, encryptedKey.EncryptionMethod?.KeyAlgorithm == XmlEncRSAOAEPUrl);
            }

            return null;
        }
    }
}

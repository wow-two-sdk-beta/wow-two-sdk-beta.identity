using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Logging;

namespace WoW.Two.Sdk.Beta.Be.Identity.DataProtection;

/// <summary>
/// Wraps each new ring key to the active ring certificate (XML encryption, RSA-OAEP over an AES key), as ASP.NET Core's
/// own certificate encryptor does, and names <see cref="CertificateRingXmlDecryptor"/> as its reader, which finds the
/// certificate among the ring's settings rather than in a certificate store.
/// </summary>
/// <param name="certificate">The active ring certificate.</param>
/// <param name="loggers">The logger factory the inner encryptor takes.</param>
internal sealed class CertificateRingXmlEncryptor(X509Certificate2 certificate, ILoggerFactory loggers) : IXmlEncryptor
{
    private readonly CertificateXmlEncryptor _inner = new(certificate, loggers);

    /// <inheritdoc />
    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
        => new(_inner.Encrypt(plaintextElement).EncryptedElement, typeof(CertificateRingXmlDecryptor));
}

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using WoW.Two.Sdk.Beta.Be.Identity.Keys;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Server;

/// <summary>A temp folder of throwaway PEM key files, deleted with the test.</summary>
internal sealed class KeyFiles : IDisposable
{
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), $"identity-keys-{Guid.NewGuid():N}");

    public KeyFiles() => System.IO.Directory.CreateDirectory(Directory);

    /// <summary>Writes a certificate of <paramref name="role"/> valid from <paramref name="notBefore"/> for <paramref name="days"/> days; returns its path.</summary>
    public string Write(string name, SigningKeyRole role, DateTimeOffset notBefore, int days = 30)
    {
        using var certificate = SigningCertificateGenerator.Create(role, notBefore, TimeSpan.FromDays(days));
        return WriteText(name, SigningCertificateGenerator.ExportPem(certificate));
    }

    /// <summary>Writes <paramref name="content"/> as a file; returns its path.</summary>
    public string WriteText(string name, string content)
    {
        var path = Path.Combine(Directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>A self-signed RSA certificate with any key size and usage, for the checks the generator never fails.</summary>
    public static X509Certificate2 Custom(int keySize, X509KeyUsageFlags usage, DateTimeOffset notBefore, int days = 30)
    {
        using var rsa = RSA.Create(keySize);
        var request = new CertificateRequest("CN=test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, critical: true));
        return request.CreateSelfSigned(notBefore, notBefore.AddDays(days));
    }

    public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
}

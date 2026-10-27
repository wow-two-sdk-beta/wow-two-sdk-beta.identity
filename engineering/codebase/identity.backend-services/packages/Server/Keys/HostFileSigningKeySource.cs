using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Hosting;
using WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

namespace WoW.Two.Sdk.Beta.Be.Identity.Keys;

/// <summary>
/// Reads the OIDC server's certificates from PEM files the host mounts (Wheelhouse's vault renders them), listed under
/// <c>Identity:Server:Keys:Signing</c> and <c>:Encryption</c>. Each file holds one certificate and its unencrypted
/// PKCS#8 private key, as <c>keys generate</c> prints them.
/// </summary>
/// <param name="options">The server options carrying the file lists.</param>
/// <param name="environment">The host environment whose content root anchors relative paths; null anchors them at the working directory.</param>
public sealed class HostFileSigningKeySource(OidcServerOptions options, IHostEnvironment? environment = null) : ISigningKeySource
{
    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">A listed file is missing, unreadable or lacks a certificate or private key; every such file is named.</exception>
    public SigningKeySet Load()
    {
        var problems = new List<string>();
        var signing = LoadRole(SigningKeyRole.Signing, options.Keys.Signing, problems);
        var encryption = LoadRole(SigningKeyRole.Encryption, options.Keys.Encryption, problems);
        if (problems.Count > 0)
        {
            foreach (var certificate in signing.Concat(encryption))
                certificate.Dispose();
            throw new InvalidOperationException($"The OIDC server's key files are not usable: {string.Join(" ", problems)}");
        }

        return new SigningKeySet(signing, encryption);
    }

    private List<X509Certificate2> LoadRole(SigningKeyRole role, IEnumerable<string> paths, List<string> problems)
    {
        var certificates = new List<X509Certificate2>();
        foreach (var configured in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var path = Path.IsPathRooted(configured) ? configured : Path.Combine(environment?.ContentRootPath ?? Directory.GetCurrentDirectory(), configured);
            if (!File.Exists(path))
            {
                problems.Add($"{role} file '{configured}' does not exist.");
                continue;
            }

            try
            {
                // One bundle holds both halves: CreateFromPem takes the first CERTIFICATE block and the first private-key block.
                var pem = File.ReadAllText(path);
                certificates.Add(X509Certificate2.CreateFromPem(pem, pem));
            }
            catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                problems.Add($"{role} file '{configured}' holds no readable certificate with its private key ({exception.GetType().Name}).");
            }
        }

        return certificates;
    }
}

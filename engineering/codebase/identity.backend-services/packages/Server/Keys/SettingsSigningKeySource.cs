using Microsoft.Extensions.Hosting;
using WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

namespace WoW.Two.Sdk.Beta.Be.Identity.Keys;

/// <summary>
/// Reads the OIDC server's certificates from its settings, <c>Identity:Server:Keys:Signing:N</c> and
/// <c>:Encryption:N</c>: each value an inline PEM bundle (a vault secret, as Wheelhouse renders it into the settings
/// file) or the path of a file holding one. Each bundle holds one certificate and its unencrypted PKCS#8 private key, as
/// <c>keys generate</c> prints it.
/// </summary>
/// <param name="options">The server options carrying the key settings.</param>
/// <param name="environment">The host environment whose content root anchors relative paths; null anchors them at the working directory.</param>
public sealed class SettingsSigningKeySource(OidcServerOptions options, IHostEnvironment? environment = null) : ISigningKeySource
{
    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">A listed value is missing, unreadable or lacks a certificate or private key; every such setting is named, never its key material.</exception>
    public SigningKeySet Load()
    {
        var problems = new List<string>();
        var root = environment?.ContentRootPath ?? Directory.GetCurrentDirectory();
        var signing = CertificateSetting.LoadAll($"{OidcServerOptions.SectionName}:Keys:{SigningKeyRole.Signing}", options.Keys.Signing, root, problems);
        var encryption = CertificateSetting.LoadAll($"{OidcServerOptions.SectionName}:Keys:{SigningKeyRole.Encryption}", options.Keys.Encryption, root, problems);
        if (problems.Count > 0)
        {
            foreach (var certificate in signing.Concat(encryption))
                certificate.Dispose();
            throw new InvalidOperationException($"The OIDC server's keys are not usable: {string.Join(" ", problems)}");
        }

        return new SigningKeySet(signing, encryption);
    }
}

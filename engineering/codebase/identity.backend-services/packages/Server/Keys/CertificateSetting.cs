using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace WoW.Two.Sdk.Beta.Be.Identity.Keys;

/// <summary>
/// Reads one certificate setting: an inline PEM bundle (the certificate, then its unencrypted PKCS#8 private key, as
/// <c>keys generate</c> prints it) or the path of a file holding one. A vault secret carries the bundle inline; a mounted
/// file carries a path.
/// </summary>
/// <remarks>A problem names the setting, and a path only when it reads as one; it never repeats an inline value, so no key material reaches a log.</remarks>
public static class CertificateSetting
{
    /// <summary>The marker that tells an inline PEM bundle from a path.</summary>
    public const string PemMarker = "-----BEGIN";

    /// <summary>The longest single-line value a problem repeats as a path; anything longer could be key material.</summary>
    private const int MaxPrintablePathLength = 256;

    /// <summary>Whether <paramref name="value"/> is an inline PEM bundle rather than a path.</summary>
    /// <param name="value">The setting's value.</param>
    public static bool IsInlinePem(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Contains(PemMarker, StringComparison.Ordinal);
    }

    /// <summary>Load the certificate a setting holds, with its private key.</summary>
    /// <param name="setting">The setting's name, such as <c>Identity:Server:Keys:Signing:0</c>, for the problem.</param>
    /// <param name="value">An inline PEM bundle or a path; a relative path resolves against <paramref name="contentRoot"/>.</param>
    /// <param name="contentRoot">The folder relative paths resolve against.</param>
    /// <param name="problem">Why nothing loaded; null on success.</param>
    /// <returns>The certificate, or null when the setting holds none.</returns>
    public static X509Certificate2? TryLoad(string setting, string value, string contentRoot, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(contentRoot);

        string pem;
        string source;
        if (IsInlinePem(value))
        {
            pem = value;
            source = $"{setting} (inline PEM)";
        }
        else
        {
            var configured = value.Trim();
            if (!IsPrintablePath(configured))
            {
                problem = $"{setting} holds neither a PEM bundle nor a file path.";
                return null;
            }

            source = $"{setting} file '{configured}'";
            var path = Path.IsPathRooted(configured) ? configured : Path.Combine(contentRoot, configured);
            if (!File.Exists(path))
            {
                problem = $"{source} does not exist.";
                return null;
            }

            try
            {
                pem = File.ReadAllText(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                problem = $"{source} cannot be read ({exception.GetType().Name}).";
                return null;
            }
        }

        try
        {
            // One bundle holds both halves: CreateFromPem takes the first CERTIFICATE block and the first private-key block.
            problem = null;
            return X509Certificate2.CreateFromPem(pem, pem);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            problem = $"{source} holds no readable certificate with its private key ({exception.GetType().Name}).";
            return null;
        }
    }

    /// <summary>Load every certificate a list setting holds, naming each entry <c>{section}:{index}</c>; blank entries are skipped.</summary>
    /// <param name="section">The list setting, such as <c>Identity:Server:Keys:Signing</c>.</param>
    /// <param name="values">Its entries.</param>
    /// <param name="contentRoot">The folder relative paths resolve against.</param>
    /// <param name="problems">Receives one problem per entry that did not load.</param>
    public static List<X509Certificate2> LoadAll(string section, IReadOnlyList<string> values, string contentRoot, ICollection<string> problems)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(problems);
        var certificates = new List<X509Certificate2>();
        for (var index = 0; index < values.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(values[index]))
                continue;

            if (TryLoad($"{section}:{index}", values[index], contentRoot, out var problem) is { } certificate)
                certificates.Add(certificate);
            else
                problems.Add(problem!);
        }

        return certificates;
    }

    private static bool IsPrintablePath(string value)
        => value.Length <= MaxPrintablePathLength && !value.Any(char.IsControl);
}

using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Hosting;
using WoW.Two.Sdk.Beta.Be.Identity.Keys;

namespace WoW.Two.Sdk.Beta.Be.Identity.DataProtection;

/// <summary>
/// The ring certificates of <see cref="DataProtectionRingOptions.Certificates"/>, loaded once at first use with the
/// problems that make them unusable: every listed one unwraps, the active one wraps new keys.
/// </summary>
internal sealed class DataProtectionRingCertificates : IDisposable
{
    private readonly Lazy<(List<X509Certificate2> Certificates, List<string> Problems)> _loaded;
    private readonly TimeProvider _time;

    /// <summary>Create the set over the ring options.</summary>
    /// <param name="options">The ring options.</param>
    /// <param name="time">The clock the active certificate is picked by.</param>
    /// <param name="environment">The host environment whose content root anchors relative paths; null anchors them at the working directory.</param>
    public DataProtectionRingCertificates(DataProtectionRingOptions options, TimeProvider time, IHostEnvironment? environment = null)
    {
        _time = time;
        _loaded = new(() => Load(options, time.GetUtcNow(), environment));
    }

    /// <summary>Load the certificates <paramref name="options"/> lists, with every problem that makes them unusable at <paramref name="now"/>.</summary>
    /// <param name="options">The ring options.</param>
    /// <param name="now">The instant validity is judged at.</param>
    /// <param name="environment">The host environment whose content root anchors relative paths; null anchors them at the working directory.</param>
    /// <returns>The loaded certificates, which the caller disposes, and the problems.</returns>
    internal static (List<X509Certificate2> Certificates, List<string> Problems) Load(DataProtectionRingOptions options, DateTimeOffset now, IHostEnvironment? environment)
    {
        var problems = new List<string>();
        var certificates = CertificateSetting.LoadAll(
            $"{DataProtectionRingOptions.SectionName}:{nameof(DataProtectionRingOptions.Certificates)}",
            options.Certificates,
            environment?.ContentRootPath ?? Directory.GetCurrentDirectory(),
            problems);
        foreach (var certificate in certificates)
            problems.AddRange(Check(certificate));
        if (certificates.Count > 0 && SigningKeySet.SelectActive(certificates, now) is null)
            problems.Add($"No Data Protection ring certificate is valid now: {string.Join(", ", certificates.Select(SigningKeyValidator.Describe))}.");

        return (certificates, problems);
    }

    /// <summary>Every listed certificate, each able to unwrap the keys it wrapped.</summary>
    public IReadOnlyList<X509Certificate2> All => _loaded.Value.Certificates;

    /// <summary>The problems that make the list unusable; empty when it is usable.</summary>
    public IReadOnlyList<string> Problems => _loaded.Value.Problems;

    /// <summary>The certificate that wraps new keys: of those valid now, the one expiring last; null when none is listed or valid.</summary>
    public X509Certificate2? Active => SigningKeySet.SelectActive(All, _time.GetUtcNow());

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_loaded.IsValueCreated)
            return;

        foreach (var certificate in _loaded.Value.Certificates)
            certificate.Dispose();
    }

    private static IEnumerable<string> Check(X509Certificate2 certificate)
    {
        using var rsa = certificate.GetRSAPrivateKey();
        if (rsa is null)
            yield return $"The Data Protection ring certificate {SigningKeyValidator.Describe(certificate)} carries no RSA private key.";
        else if (rsa.KeySize < SigningKeyValidator.MinimumKeySize)
            yield return $"The Data Protection ring certificate {SigningKeyValidator.Describe(certificate)} has a {rsa.KeySize}-bit key; {SigningKeyValidator.MinimumKeySize} bits is the floor.";

        if (certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault() is { } usage && !usage.KeyUsages.HasFlag(X509KeyUsageFlags.KeyEncipherment))
            yield return $"The Data Protection ring certificate {SigningKeyValidator.Describe(certificate)} lacks the KeyEncipherment key usage.";
    }
}

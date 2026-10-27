namespace WoW.Two.Sdk.Beta.Be.Identity.DataProtection;

/// <summary>Holds the instance's Data Protection ring settings: its application name and the certificates that wrap it.</summary>
/// <remarks>Set in code with <c>.AddDataProtectionRing(o => …)</c> or in the host section <c>Identity:DataProtection</c>, which is applied last.</remarks>
public sealed record DataProtectionRingOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Identity:DataProtection";

    /// <summary>Gets or sets the name that isolates this instance's payloads from other apps on the same ring. Default <c>identity</c>.</summary>
    /// <remarks>Every replica of one instance shares it; changing it voids every cookie the ring sealed.</remarks>
    public string ApplicationName { get; set; } = "identity";

    /// <summary>
    /// Gets or sets the certificates that wrap the ring's keys at rest, each an inline PEM bundle (a vault secret) or a
    /// file path, as <c>keys generate --role data-protection</c> prints it. Of those valid now, the one expiring last
    /// wraps new keys; every listed one unwraps, so a rotation keeps the old one until the keys it wrapped retire.
    /// </summary>
    /// <remarks>A deployed boot refuses an empty list; a local run may leave it empty and stores the ring unwrapped.</remarks>
    public List<string> Certificates { get; set; } = [];
}

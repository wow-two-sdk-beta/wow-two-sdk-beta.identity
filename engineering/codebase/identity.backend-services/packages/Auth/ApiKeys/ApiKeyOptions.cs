namespace WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;

/// <summary>Holds the shape of a product's API key secrets and how the scheme reads them.</summary>
public sealed class ApiKeyOptions
{
    /// <summary>Holds the letters and digits a secret draws from — URL- and header-safe.</summary>
    public const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>Holds how many check characters end a new secret: the marker and random part's hash, so a mistyped or invented key
    /// is refused before any lookup and a scanner can confirm a leaked one.</summary>
    public const int ChecksumLength = 6;

    /// <summary>Gets or sets the marker every secret starts with, so a leaked key is recognizable — such as <c>tf_</c>.</summary>
    public string Marker { get; set; } = "key_";

    /// <summary>Gets or sets how many random characters follow the marker; default 32 (about 190 bits).</summary>
    public int RandomLength { get; set; } = 32;

    /// <summary>
    /// Gets or sets whether a secret without the check characters, as keys were minted before them, is still looked up; default
    /// <c>true</c>. Turn it off once every such key is rotated, and a key of that shape fails like a malformed one.
    /// </summary>
    public bool AcceptsKeysWithoutChecksum { get; set; } = true;

    /// <summary>
    /// Gets or sets the issuer a key's principal names as <c>iss</c>, such as this host's own URL, so a key's caller is told apart by
    /// (<c>iss</c>, <c>sub</c>) beside tokens from other issuers; null names none.
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>Gets or sets how many random characters the display prefix shows after the marker; default 8.</summary>
    public int VisibleLength { get; set; } = 8;

    /// <summary>Gets or sets how stale a key's last-use instant may grow before the store is asked to write it again.</summary>
    public TimeSpan TouchInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Gets or sets the header a client may send the secret in; a Bearer token always works too.</summary>
    public string HeaderName { get; set; } = ApiKeyAuthenticationDefaults.HeaderName;
}

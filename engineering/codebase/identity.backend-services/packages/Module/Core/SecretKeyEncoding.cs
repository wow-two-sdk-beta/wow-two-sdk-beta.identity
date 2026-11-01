namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>Decodes a configured secret key written as standard base64 or base64url, so a key a deploy tool generates in either alphabet is usable.</summary>
/// <remarks>
///   - standard base64 (<c>+</c>, <c>/</c>) and base64url (<c>-</c>, <c>_</c>) are both accepted, with or without <c>=</c> padding
///   - a key that mixes the two alphabets, or has a length no base64 text can have, does not decode
///   - surrounding whitespace is ignored; the key itself is never logged or returned in a message
/// </remarks>
internal static class SecretKeyEncoding
{
    /// <summary>Decodes <paramref name="key"/> to its bytes.</summary>
    /// <param name="key">The configured key text.</param>
    /// <param name="bytes">The decoded key, or an empty array when the text does not decode.</param>
    /// <returns>Whether the text is a valid standard base64 or base64url key.</returns>
    public static bool TryDecode(string? key, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var text = key.Trim().TrimEnd('=');
        var isUrlAlphabet = text.AsSpan().ContainsAny('-', '_');
        var isStandardAlphabet = text.AsSpan().ContainsAny('+', '/');
        if (isUrlAlphabet && isStandardAlphabet)
            return false;

        if (isUrlAlphabet)
            text = text.Replace('-', '+').Replace('_', '/');

        var padding = (4 - (text.Length % 4)) % 4;
        if (padding == 3)
            return false;

        var padded = text + new string('=', padding);
        var buffer = new byte[padded.Length];
        if (!Convert.TryFromBase64String(padded, buffer, out var written))
            return false;

        bytes = buffer[..written];
        return true;
    }

    /// <summary>Gets the decoded length of <paramref name="key"/>.</summary>
    /// <param name="key">The configured key text.</param>
    /// <returns>The byte count, or <c>0</c> when the text is unset or does not decode.</returns>
    public static int DecodedLength(string? key) => TryDecode(key, out var bytes) ? bytes.Length : 0;

    /// <summary>Decodes <paramref name="key"/>, naming the setting in the failure.</summary>
    /// <param name="key">The configured key text.</param>
    /// <param name="setting">The setting path, for the message.</param>
    /// <returns>The decoded key.</returns>
    /// <exception cref="InvalidOperationException">The key is unset or is not base64 or base64url.</exception>
    public static byte[] Decode(string? key, string setting) =>
        TryDecode(key, out var bytes) ? bytes : throw new InvalidOperationException($"{setting} must be base64 or base64url.");
}

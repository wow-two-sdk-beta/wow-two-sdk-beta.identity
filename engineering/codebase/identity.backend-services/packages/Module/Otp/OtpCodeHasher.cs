using System.Security.Cryptography;
using System.Text;
using WoW.Two.Sdk.Beta.Be.Identity.Core;

namespace WoW.Two.Sdk.Beta.Be.Identity.Otp;

/// <summary>
/// Keys every stored OTP value with HMAC-SHA256, so a database copy reveals no code, subject or origin and a six-digit
/// code cannot be guessed offline. The key is <see cref="OtpOptions.HashKey"/>; without one, the process draws its own,
/// which serves one process only and loses live codes on restart.
/// </summary>
public sealed class OtpCodeHasher
{
    private readonly byte[] _key;

    /// <summary>Creates the hasher over the configured key, or a random one when none is set.</summary>
    /// <param name="options">The OTP options carrying <see cref="OtpOptions.HashKey"/>.</param>
    public OtpCodeHasher(OtpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _key = string.IsNullOrWhiteSpace(options.HashKey) ? RandomNumberGenerator.GetBytes(32) : SecretKeyEncoding.Decode(options.HashKey, "Identity:Otp:HashKey");
        IsKeyConfigured = !string.IsNullOrWhiteSpace(options.HashKey);
    }

    /// <summary>Whether the key comes from the settings, so every instance shares it.</summary>
    public bool IsKeyConfigured { get; }

    /// <summary>The digest a subject is stored and looked up by.</summary>
    /// <param name="subject">The consumer-defined identity key.</param>
    public string Subject(string subject) => Digest("subject", subject);

    /// <summary>The digest an origin, such as a client address, is stored and looked up by.</summary>
    /// <param name="origin">The origin.</param>
    public string Origin(string origin) => Digest("origin", origin);

    /// <summary>The digest of <paramref name="code"/> for one subject and scope; case, spaces and dashes are ignored.</summary>
    /// <param name="subjectHash">The subject's digest.</param>
    /// <param name="scope">The scope.</param>
    /// <param name="code">The code as generated or typed.</param>
    public string Code(string subjectHash, string scope, string code) => Digest("code", string.Concat(scope, "\n", subjectHash, "\n", Canonical(code)));

    /// <summary>Compares two digests in constant time.</summary>
    /// <param name="expected">The stored digest.</param>
    /// <param name="actual">The digest of what was typed.</param>
    public static bool Matches(string expected, string actual)
        => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual));

    private string Digest(string purpose, string value)
        => Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(string.Concat(purpose, "\n", value))));

    private static string Canonical(string code)
        => code.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
}

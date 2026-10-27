using System.Globalization;

namespace WoW.Two.Sdk.Beta.Be.Identity.Keys;

/// <summary>
/// The owner's key command, run inside the image before any host boots:
/// <c>keys generate --role signing|encryption [--days N] [--not-before &lt;date&gt;]</c> prints one PEM bundle to
/// standard output for the vault, and the certificate's thumbprint and dates to standard error.
/// </summary>
/// <remarks>Never run it in CI or from an agent: the private key it prints is the server's signing or encryption key.</remarks>
public static class SigningKeyCommand
{
    /// <summary>The command's usage line.</summary>
    public const string Usage = "usage: keys generate --role signing|encryption [--days N] [--not-before <yyyy-MM-dd[THH:mm:ssZ]>]";

    private const int MaxDays = 3650;

    /// <summary>Run the command.</summary>
    /// <param name="arguments">The arguments after <c>keys</c>, such as <c>generate --role signing --days 120</c>.</param>
    /// <param name="output">Receives the PEM bundle.</param>
    /// <param name="error">Receives the summary line, or the usage on a mistake.</param>
    /// <param name="time">The clock a missing <c>--not-before</c> starts from.</param>
    /// <returns><c>0</c> printed · <c>2</c> a usage mistake.</returns>
    public static int Run(string[] arguments, TextWriter output, TextWriter error, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(time);

        if (!TryParse(arguments, time, out var role, out var notBefore, out var lifetime, out var mistake))
        {
            error.WriteLine(mistake);
            error.WriteLine(Usage);
            return 2;
        }

        using var certificate = SigningCertificateGenerator.Create(role, notBefore, lifetime);
        output.Write(SigningCertificateGenerator.ExportPem(certificate));
        error.WriteLine($"Generated {(role == SigningKeyRole.Signing ? "a signing" : "an encryption")} certificate {SigningKeyValidator.Describe(certificate)}.");
        return 0;
    }

    private static bool TryParse(
        string[] arguments,
        TimeProvider time,
        out SigningKeyRole role,
        out DateTimeOffset notBefore,
        out TimeSpan lifetime,
        out string mistake)
    {
        role = default;
        notBefore = time.GetUtcNow();
        lifetime = default;
        mistake = string.Empty;
        if (arguments is not ["generate", .. var options] || options.Length % 2 != 0)
        {
            mistake = "Expected 'generate' followed by option and value pairs.";
            return false;
        }

        SigningKeyRole? parsedRole = null;
        int? days = null;
        for (var i = 0; i < options.Length; i += 2)
        {
            var (name, value) = (options[i], options[i + 1]);
            switch (name)
            {
                case "--role" when Enum.TryParse<SigningKeyRole>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed):
                    parsedRole = parsed;
                    break;
                case "--days" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count is > 0 and <= MaxDays:
                    days = count;
                    break;
                case "--not-before" when DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var start):
                    notBefore = start;
                    break;
                default:
                    mistake = $"'{name} {value}' is not a valid option; --days takes 1–{MaxDays}.";
                    return false;
            }
        }

        if (parsedRole is not { } chosen)
        {
            mistake = "--role is required.";
            return false;
        }

        role = chosen;
        lifetime = days is { } d ? TimeSpan.FromDays(d) : SigningCertificateGenerator.DefaultLifetime(chosen);
        return true;
    }
}

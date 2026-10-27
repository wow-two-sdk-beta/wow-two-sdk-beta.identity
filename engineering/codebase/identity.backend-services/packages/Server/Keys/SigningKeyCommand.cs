using System.Globalization;
using System.Security.Cryptography.X509Certificates;

namespace WoW.Two.Sdk.Beta.Be.Identity.Keys;

/// <summary>
/// The owner's key command, run inside the image before any host boots:
/// <c>keys generate --role signing|encryption|data-protection [--days N] [--not-before &lt;date&gt;]</c> prints one PEM
/// bundle to standard output, to paste into the vault secret as is, and the certificate's thumbprint and dates to
/// standard error.
/// </summary>
/// <remarks>Never run it in CI or from an agent: the private key it prints is the server's signing or encryption key, or the ring's.</remarks>
public static class SigningKeyCommand
{
    /// <summary>The command's usage line.</summary>
    public const string Usage = "usage: keys generate --role signing|encryption|data-protection [--days N] [--not-before <yyyy-MM-dd[THH:mm:ssZ]>]";

    /// <summary>The role value that makes a Data Protection ring certificate.</summary>
    public const string DataProtectionRole = "data-protection";

    private const int MaxDays = 3650;

    /// <summary>What a run generates.</summary>
    private enum Target
    {
        Signing,
        Encryption,
        DataProtection,
    }

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

        if (!TryParse(arguments, time, out var target, out var notBefore, out var lifetime, out var mistake))
        {
            error.WriteLine(mistake);
            error.WriteLine(Usage);
            return 2;
        }

        using var certificate = Create(target, notBefore, lifetime);
        output.Write(SigningCertificateGenerator.ExportPem(certificate));
        error.WriteLine($"Generated {Describe(target)} certificate {SigningKeyValidator.Describe(certificate)}.");
        return 0;
    }

    private static X509Certificate2 Create(Target target, DateTimeOffset notBefore, TimeSpan lifetime) => target switch
    {
        Target.Signing => SigningCertificateGenerator.Create(SigningKeyRole.Signing, notBefore, lifetime),
        Target.Encryption => SigningCertificateGenerator.Create(SigningKeyRole.Encryption, notBefore, lifetime),
        _ => SigningCertificateGenerator.CreateDataProtection(notBefore, lifetime),
    };

    private static string Describe(Target target) => target switch
    {
        Target.Signing => "a signing",
        Target.Encryption => "an encryption",
        _ => "a Data Protection ring",
    };

    private static TimeSpan DefaultLifetime(Target target) => target switch
    {
        Target.Signing => SigningCertificateGenerator.DefaultSigningLifetime,
        Target.Encryption => SigningCertificateGenerator.DefaultEncryptionLifetime,
        _ => SigningCertificateGenerator.DefaultDataProtectionLifetime,
    };

    private static bool TryParseTarget(string value, out Target target)
    {
        if (string.Equals(value, DataProtectionRole, StringComparison.OrdinalIgnoreCase))
        {
            target = Target.DataProtection;
            return true;
        }

        var parsed = Enum.TryParse<SigningKeyRole>(value, ignoreCase: true, out var role) && Enum.IsDefined(role);
        target = role == SigningKeyRole.Signing ? Target.Signing : Target.Encryption;
        return parsed;
    }

    private static bool TryParse(
        string[] arguments,
        TimeProvider time,
        out Target target,
        out DateTimeOffset notBefore,
        out TimeSpan lifetime,
        out string mistake)
    {
        target = default;
        notBefore = time.GetUtcNow();
        lifetime = default;
        mistake = string.Empty;
        if (arguments is not ["generate", .. var options] || options.Length % 2 != 0)
        {
            mistake = "Expected 'generate' followed by option and value pairs.";
            return false;
        }

        Target? parsedTarget = null;
        int? days = null;
        for (var i = 0; i < options.Length; i += 2)
        {
            var (name, value) = (options[i], options[i + 1]);
            switch (name)
            {
                case "--role" when TryParseTarget(value, out var parsed):
                    parsedTarget = parsed;
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

        if (parsedTarget is not { } chosen)
        {
            mistake = "--role is required.";
            return false;
        }

        target = chosen;
        lifetime = days is { } d ? TimeSpan.FromDays(d) : DefaultLifetime(chosen);
        return true;
    }
}

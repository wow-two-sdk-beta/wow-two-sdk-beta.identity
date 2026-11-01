using Microsoft.Extensions.Options;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;

/// <summary>
/// Provides the instance's phone-number rules: reading a typed number as E.164, the countries a number belongs to, and
/// whether <see cref="PhoneOptions.AllowedCountries"/> admits it.
/// </summary>
/// <param name="options">The phone options; <c>Identity:Phone</c> reloads live.</param>
public sealed class PhoneNumberPolicy(IOptionsMonitor<PhoneOptions> options)
{
    private const int MinDigits = 8;
    private const int MaxDigits = 15;

    /// <summary>
    /// Reads <paramref name="input"/> as E.164 (<c>+</c> and 8–15 digits, the first not 0), ignoring spaces, dashes, dots
    /// and parentheses and taking a leading <c>00</c> as <c>+</c>; null when it is no such number.
    /// </summary>
    /// <param name="input">The number as typed.</param>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var compact = new string([.. input.Trim().Where(c => c is not (' ' or '-' or '.' or '(' or ')'))]);
        if (compact.StartsWith("00", StringComparison.Ordinal))
            compact = "+" + compact[2..];

        return compact.Length > 1 && compact[0] == '+' && IsDialable(compact.AsSpan(1)) ? compact : null;
    }

    /// <summary>Reads a number Telegram reports in a shared contact, which may come without its <c>+</c>; null when it is no E.164 number.</summary>
    /// <param name="contactNumber">The contact's number.</param>
    public static string? NormalizeContact(string? contactNumber)
    {
        if (string.IsNullOrWhiteSpace(contactNumber))
            return null;

        var trimmed = contactNumber.Trim();
        return Normalize(trimmed.StartsWith('+') ? trimmed : "+" + trimmed);
    }

    /// <summary>The ISO codes of the countries an E.164 number belongs to: those whose dialling code is its longest known prefix.</summary>
    /// <param name="e164">The number in E.164.</param>
    public static IReadOnlyList<string> CountriesOf(string e164)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(e164);
        var digits = e164.TrimStart('+');
        var matches = PhoneCountryConstants.DialCodes.Where(country => digits.StartsWith(country.Value, StringComparison.Ordinal)).ToList();
        if (matches.Count == 0)
            return [];

        var longest = matches.Max(country => country.Value.Length);
        return [.. matches.Where(country => country.Value.Length == longest).Select(country => country.Key)];
    }

    /// <summary>Whether the instance accepts <paramref name="e164"/>: it belongs to an allowed country, or to any known one when none is listed.</summary>
    /// <param name="e164">The number in E.164.</param>
    public bool IsAllowed(string e164)
    {
        var countries = CountriesOf(e164);
        var allowed = options.CurrentValue.AllowedCountries;
        return allowed.Count == 0
            ? countries.Count > 0
            : countries.Any(country => allowed.Contains(country, StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsDialable(ReadOnlySpan<char> digits)
    {
        if (digits.Length is < MinDigits or > MaxDigits || digits[0] == '0')
            return false;

        foreach (var digit in digits)
        {
            if (!char.IsAsciiDigit(digit))
                return false;
        }

        return true;
    }
}

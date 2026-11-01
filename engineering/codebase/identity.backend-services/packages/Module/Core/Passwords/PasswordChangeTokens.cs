using System.Globalization;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;

/// <summary>Packs a person's id beside a secret into one opaque value, so a caller holding only the value can be found.</summary>
/// <remarks>The shape is <c>{userId as 32 hex digits}.{secret}</c>; the secret decides, the id only names whose secret to check.</remarks>
internal static class PasswordChangeTokens
{
    private const char Separator = '.';

    /// <summary>Joins <paramref name="userId"/> and <paramref name="secret"/>.</summary>
    /// <param name="userId">The person.</param>
    /// <param name="secret">The ticket or the one-time code.</param>
    internal static string Compose(Guid userId, string secret)
        => string.Concat(userId.ToString("N", CultureInfo.InvariantCulture), Separator.ToString(), secret);

    /// <summary>Splits a value <see cref="Compose"/> made; false for anything else.</summary>
    /// <param name="value">The presented value.</param>
    /// <param name="userId">The person it names.</param>
    /// <param name="secret">The secret after the id.</param>
    internal static bool TryParse(string? value, out Guid userId, out string secret)
    {
        userId = Guid.Empty;
        secret = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var separator = value.IndexOf(Separator, StringComparison.Ordinal);
        if (separator != 32 || separator == value.Length - 1)
            return false;

        if (!Guid.TryParseExact(value.AsSpan(0, separator), "N", out userId))
            return false;

        secret = value[(separator + 1)..];
        return true;
    }
}

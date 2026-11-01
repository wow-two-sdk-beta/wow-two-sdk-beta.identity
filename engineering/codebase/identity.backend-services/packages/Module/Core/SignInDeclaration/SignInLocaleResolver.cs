using Microsoft.Net.Http.Headers;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;

/// <summary>Picks the locale of a declaration's messages: the one the request names, else the browser's best, else <c>en</c>.</summary>
internal static class SignInLocaleResolver
{
    /// <summary>The locale used when the request names none the pages offer.</summary>
    public const string DefaultLocale = "en";

    /// <summary>The locales the pages offer, in display order.</summary>
    public static IReadOnlyList<string> Locales { get; } = ["uz", "ru", "en"];

    /// <summary>
    /// The first offered locale among <paramref name="requested"/> (<c>?locale=</c>), then the <c>Accept-Language</c> ranges by
    /// quality; a region or script never matters (<c>ru-RU</c> is <c>ru</c>), and <c>*</c> or a zero quality never picks one.
    /// </summary>
    /// <param name="requested">The locale the request names, or null.</param>
    /// <param name="acceptLanguage">The parsed <c>Accept-Language</c> header.</param>
    public static string Resolve(string? requested, IEnumerable<StringWithQualityHeaderValue> acceptLanguage)
    {
        ArgumentNullException.ThrowIfNull(acceptLanguage);
        if (Offered(requested) is { } named)
            return named;

        var ranges = acceptLanguage
            .Select((range, index) => (Tag: range.Value.Value, Quality: range.Quality ?? 1, Index: index))
            .Where(range => range.Quality > 0)
            .OrderByDescending(range => range.Quality)
            .ThenBy(range => range.Index);
        foreach (var range in ranges)
        {
            if (Offered(range.Tag) is { } accepted)
                return accepted;
        }

        return DefaultLocale;
    }

    private static string? Offered(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var language = tag.Trim().Split('-', '_')[0];
        return Locales.FirstOrDefault(locale => string.Equals(locale, language, StringComparison.OrdinalIgnoreCase));
    }
}

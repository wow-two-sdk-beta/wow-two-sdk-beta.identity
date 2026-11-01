namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;

/// <summary>Provides the rules the look settings of <c>Identity:Pages</c> must meet.</summary>
public static class HostedPageLookRules
{
    /// <summary>Whether <paramref name="theme"/> is a registry id the pages turn into a <c>theme-{id}</c> class: lower-case letters and digits in hyphen-joined words.</summary>
    /// <param name="theme">The theme setting.</param>
    public static bool IsThemeName(string? theme)
    {
        if (string.IsNullOrEmpty(theme) || theme.Length > 64 || theme[0] == '-' || theme[^1] == '-' || theme.Contains("--", StringComparison.Ordinal))
            return false;

        return theme.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
    }

    /// <summary>
    /// Whether <paramref name="url"/> may sit behind the card: a path on the instance's own origin (one leading <c>/</c>, never
    /// <c>//</c> or a backslash a browser reads as another host) or an absolute HTTPS URL with a host and no user info;
    /// neither may carry a control character, a quote or a space.
    /// </summary>
    /// <param name="url">The background setting.</param>
    public static bool IsBackgroundUrl(string? url)
    {
        if (string.IsNullOrEmpty(url) || url.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '"' or '\'' or '\\'))
            return false;

        if (url[0] == '/')
            return !url.StartsWith("//", StringComparison.Ordinal) && Uri.IsWellFormedUriString(url, UriKind.Relative);

        return Uri.TryCreate(url, UriKind.Absolute, out var absolute)
            && absolute.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrEmpty(absolute.Host)
            && string.IsNullOrEmpty(absolute.UserInfo);
    }
}

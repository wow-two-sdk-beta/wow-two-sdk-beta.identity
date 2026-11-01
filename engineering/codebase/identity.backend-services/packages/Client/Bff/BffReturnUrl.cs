namespace WoW.Two.Sdk.Beta.Be.Identity.Bff;

/// <summary>Validates where the BFF sends a browser back to: local paths only, so a sign-in never becomes an open redirect.</summary>
internal static class BffReturnUrl
{
    /// <summary>Holds the path used for a missing or foreign return URL.</summary>
    public const string Root = "/";

    /// <summary>
    /// Whether <paramref name="url"/> is a local path: it starts with one <c>/</c>, never <c>//</c> or <c>/\</c> (a
    /// protocol-relative URL), and holds no control character or backslash a browser could read as another host.
    /// </summary>
    /// <param name="url">The candidate.</param>
    public static bool IsLocal(string? url)
    {
        if (string.IsNullOrEmpty(url) || url[0] != '/')
            return false;
        if (url.Length > 1 && (url[1] == '/' || url[1] == '\\'))
            return false;

        return !url.Any(c => char.IsControl(c) || c == '\\');
    }

    /// <summary><paramref name="url"/> when it is local, else <see cref="Root"/>.</summary>
    /// <param name="url">The requested return URL.</param>
    public static string Normalize(string? url) => IsLocal(url) ? url! : Root;
}

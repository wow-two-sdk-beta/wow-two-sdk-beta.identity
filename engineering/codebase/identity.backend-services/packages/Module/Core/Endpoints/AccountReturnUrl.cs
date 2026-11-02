namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>Judges the return URLs the account API carries through its emails: local paths only, so a link never becomes an open redirect.</summary>
public static class AccountReturnUrl
{
    /// <summary>The longest return URL an emailed link carries.</summary>
    public const int MaxLength = 2048;

    /// <summary>
    /// Whether <paramref name="url"/> is a local path of at most <see cref="MaxLength"/> characters: it starts with one
    /// <c>/</c>, never <c>//</c> or <c>/\</c> (a protocol-relative URL), so it names no scheme or host, and holds no control
    /// character or backslash a browser could read as another host.
    /// </summary>
    /// <param name="url">The candidate.</param>
    public static bool IsLocal(string? url)
    {
        if (string.IsNullOrEmpty(url) || url.Length > MaxLength || url[0] != '/')
            return false;
        if (url.Length > 1 && (url[1] == '/' || url[1] == '\\'))
            return false;

        return !url.Any(c => char.IsControl(c) || c == '\\');
    }

    /// <summary><paramref name="url"/> when it is local, else null.</summary>
    /// <param name="url">The requested return URL.</param>
    public static string? LocalOrNull(string? url) => IsLocal(url) ? url : null;
}

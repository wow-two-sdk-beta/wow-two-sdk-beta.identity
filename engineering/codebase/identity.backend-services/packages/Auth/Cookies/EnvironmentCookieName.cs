namespace WoW.Two.Sdk.Beta.Be.Identity.Cookies;

/// <summary>Names a cookie for the deploy environment it serves. Cookies ignore the port, so two environments on one
/// host name (prod on 443, dev on 8443) would overwrite each other's sign-in: the primary environment keeps the plain
/// name, any other deployed environment adds its own.</summary>
public static class EnvironmentCookieName
{
    /// <summary>The environment that keeps the plain name unless a host names another: <c>prod</c>.</summary>
    public const string DefaultPrimaryEnvironment = "prod";

    /// <summary>Names <paramref name="cookieName"/> for <paramref name="environment"/>: the plain name on a local run
    /// (empty), in <paramref name="primaryEnvironment"/>, and for a value that is not ASCII letters and digits alone (so
    /// nothing but a plain suffix reaches the cookie header); <c>{cookieName}.{environment}</c>, lowercase, otherwise.</summary>
    /// <param name="cookieName">The plain cookie name, such as <c>.wheelhouse.auth</c>.</param>
    /// <param name="environment">The deploy environment, such as <c>dev</c>; <c>null</c> or empty on a local run.</param>
    /// <param name="primaryEnvironment">The environment that keeps the plain name.</param>
    /// <returns>The cookie name, such as <c>.wheelhouse.auth</c> or <c>.wheelhouse.auth.dev</c>.</returns>
    public static string For(string cookieName, string? environment, string primaryEnvironment = DefaultPrimaryEnvironment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cookieName);

        var name = environment?.Trim().ToLowerInvariant() ?? "";
        var primary = primaryEnvironment?.Trim().ToLowerInvariant() ?? "";
        if (name.Length == 0 || name == primary || !name.All(char.IsAsciiLetterOrDigit))
            return cookieName;
        return cookieName + "." + name;
    }
}

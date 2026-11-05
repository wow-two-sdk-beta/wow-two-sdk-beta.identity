namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>
/// Words a user agent for the person answering for a device, such as <c>Chrome on macOS</c>. Only these fixed names reach them:
/// whoever is signing in writes the user agent, so its text must not read as part of the bot's question.
/// </summary>
internal static class DeviceLabel
{
    // Order matters: Edge and Opera also name Chrome, and Chrome also names Safari.
    private static readonly (string Token, string Name)[] Browsers =
    [
        ("Edg/", "Edge"), ("OPR/", "Opera"), ("SamsungBrowser/", "Samsung Internet"), ("YaBrowser/", "Yandex Browser"),
        ("Firefox/", "Firefox"), ("FxiOS/", "Firefox"), ("Chrome/", "Chrome"), ("CriOS/", "Chrome"), ("Safari/", "Safari"),
    ];

    // Order matters: Android names Linux, and iOS names Mac OS X.
    private static readonly (string Token, string Name)[] Systems =
    [
        ("iPhone", "iPhone"), ("iPad", "iPad"), ("Android", "Android"), ("Windows", "Windows"), ("Mac OS X", "macOS"), ("Linux", "Linux"),
    ];

    /// <summary>The label of <paramref name="userAgent"/>: its known browser and system; null when it names neither.</summary>
    /// <param name="userAgent">The request's user agent.</param>
    internal static string? Of(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return null;

        var browser = FirstIn(userAgent, Browsers);
        var system = FirstIn(userAgent, Systems);
        return browser is not null && system is not null ? $"{browser} on {system}" : browser ?? system;
    }

    private static string? FirstIn(string userAgent, (string Token, string Name)[] names)
        => names.FirstOrDefault(name => userAgent.Contains(name.Token, StringComparison.Ordinal)).Name;
}

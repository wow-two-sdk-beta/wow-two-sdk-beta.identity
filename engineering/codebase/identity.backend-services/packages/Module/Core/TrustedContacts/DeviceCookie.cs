using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>
/// Names a browser for new device confirmation: 32 random bytes in the <see cref="Name"/> cookie, <c>HttpOnly</c>, renewed for
/// 400 days at each use. The instance stores only its SHA-256. A bearer client keeps no cookie, so it gets a one-off value
/// each time and stays new.
/// </summary>
public static class DeviceCookie
{
    /// <summary>The cookie's name.</summary>
    public const string Name = "__Host-identity-device";

    private const int ValueBytes = 32;

    private static readonly int ValueLength = Base64Url.GetEncodedLength(ValueBytes);

    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(400);

    /// <summary>The browser's device value, setting the cookie when <paramref name="isCookieClient"/>.</summary>
    /// <param name="http">The current request.</param>
    /// <param name="isCookieClient">Whether the client signs in with cookies; a bearer client gets a one-off value and no cookie.</param>
    public static string Ensure(HttpContext http, bool isCookieClient)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (!isCookieClient)
            return NewValue();

        var value = Read(http) ?? NewValue();
        http.Response.Cookies.Append(
            Name,
            value,
            new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/", MaxAge = Lifetime, IsEssential = true });
        return value;
    }

    /// <summary>The device value the request's cookie carries; null with no cookie of the right shape.</summary>
    /// <param name="http">The current request.</param>
    public static string? Read(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return http.Request.Cookies[Name] is { } sent && sent.Length == ValueLength ? sent : null;
    }

    /// <summary>A new random value, for a device or a poll token.</summary>
    internal static string NewValue() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ValueBytes));

    /// <summary>The hex SHA-256 a value is stored and looked up by.</summary>
    /// <param name="value">The cookie's value or the poll token.</param>
    internal static string HashOf(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

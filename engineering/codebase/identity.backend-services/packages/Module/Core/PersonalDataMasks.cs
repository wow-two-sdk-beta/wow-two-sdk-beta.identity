using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>
/// Shortens the personal data the sessions and the sign-in log keep about a request: the address to its network, the
/// typed login to a hint, the user agent to a bounded length.
/// </summary>
internal static class PersonalDataMasks
{
    /// <summary>The longest user agent kept.</summary>
    internal const int MaxUserAgentLength = 256;

    /// <summary>The longest masked subject kept.</summary>
    internal const int MaxSubjectLength = 100;

    /// <summary>
    /// The address's network: IPv4 keeps its first three octets (<c>203.0.113.0</c>), IPv6 its first 48 bits
    /// (<c>2001:db8:85a3::</c>); an IPv4-mapped IPv6 address is read as IPv4. Null stays null.
    /// </summary>
    /// <param name="address">The remote address.</param>
    internal static string? MaskAddress(IPAddress? address)
    {
        if (address is null)
            return null;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        var bytes = address.GetAddressBytes();
        var keep = address.AddressFamily == AddressFamily.InterNetwork ? 3 : 6;
        for (var i = keep; i < bytes.Length; i++)
            bytes[i] = 0;

        return new IPAddress(bytes).ToString();
    }

    /// <summary>
    /// A hint of the login a person typed: an email keeps its first character and its domain (<c>a***@example.com</c>), a
    /// phone number its first four and last two characters (<c>+998*******45</c>), anything else its first two
    /// characters (<c>al***</c>). Blank stays null.
    /// </summary>
    /// <param name="subject">The login as typed.</param>
    internal static string? MaskSubject(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return null;

        var value = subject.Trim();
        string masked;
        if (value.IndexOf('@', StringComparison.Ordinal) is var at and > 0)
            masked = $"{value[0]}***{value[at..]}";
        else if (IsPhoneNumber(value) && value.Length > 6)
            masked = $"{value[..4]}{new string('*', value.Length - 6)}{value[^2..]}";
        else
            masked = $"{value[..Math.Min(2, value.Length)]}***";

        return masked.Length <= MaxSubjectLength ? masked : masked[..MaxSubjectLength];
    }

    /// <summary>The user agent cut to <see cref="MaxUserAgentLength"/> characters; blank stays null.</summary>
    /// <param name="userAgent">The header value.</param>
    internal static string? TrimUserAgent(string? userAgent)
        => string.IsNullOrWhiteSpace(userAgent) ? null : userAgent.Length <= MaxUserAgentLength ? userAgent : userAgent[..MaxUserAgentLength];

    /// <summary>The masked address and the trimmed user agent of the current request; both null outside a request.</summary>
    /// <param name="http">The current request, or null.</param>
    internal static (string? IpMasked, string? UserAgent) Describe(HttpContext? http)
        => http is null
            ? (null, null)
            : (MaskAddress(http.Connection.RemoteIpAddress), TrimUserAgent(http.Request.Headers.UserAgent.ToString()));

    /// <summary>The W3C trace id of the current activity, or null.</summary>
    internal static string? TraceId() => Activity.Current?.TraceId.ToHexString();

    private static bool IsPhoneNumber(string value)
        => value.All(c => char.IsAsciiDigit(c) || c is '+' or ' ' or '-' or '(' or ')') && value.Count(char.IsAsciiDigit) >= 7;
}

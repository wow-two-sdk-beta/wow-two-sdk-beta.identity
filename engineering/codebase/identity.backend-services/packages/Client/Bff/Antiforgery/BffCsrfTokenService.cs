using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Antiforgery;

/// <summary>
/// Provides the antiforgery token bound to a session's <c>sid</c>: the session route hands it to the SPA, which echoes it
/// in <see cref="BffHeaderConstants.CsrfToken"/>; a token of another session, or none, fails.
/// </summary>
/// <param name="protection">The application's Data Protection provider.</param>
internal sealed class BffCsrfTokenService(IDataProtectionProvider protection)
{
    /// <summary>Holds the protector of antiforgery tokens.</summary>
    private readonly IDataProtector _protector = protection.CreateProtector("WoW.Two.Sdk.Beta.Be.Identity.Bff.Csrf", "v1");

    /// <summary>A token for the session <paramref name="sessionId"/>.</summary>
    /// <param name="sessionId">The session's <c>sid</c>.</param>
    public string Create(string sessionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        return _protector.Protect(sessionId);
    }

    /// <summary>Whether <paramref name="token"/> was made for the session <paramref name="sessionId"/>.</summary>
    /// <param name="sessionId">The session's <c>sid</c>.</param>
    /// <param name="token">The header's value; null or empty fails.</param>
    public bool IsValid(string sessionId, string? token)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        if (string.IsNullOrEmpty(token))
            return false;

        try
        {
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(_protector.Unprotect(token)), Encoding.UTF8.GetBytes(sessionId));
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}

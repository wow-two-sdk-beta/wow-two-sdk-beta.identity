using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

/// <summary>Protects a session's state with Data Protection before it reaches the store, and reads it back.</summary>
/// <param name="protection">The application's Data Protection provider; several instances share its key ring.</param>
internal sealed class BffSessionProtector(IDataProtectionProvider protection)
{
    /// <summary>Holds the protector of session payloads.</summary>
    private readonly IDataProtector _protector = protection.CreateProtector("WoW.Two.Sdk.Beta.Be.Identity.Bff.Session", "v1");

    /// <summary>The encrypted form of <paramref name="state"/>.</summary>
    /// <param name="state">The session state.</param>
    public byte[] Protect(BffSessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(state));
    }

    /// <summary>The state inside <paramref name="payload"/>, or null when it no longer decrypts (a rotated key ring) or reads.</summary>
    /// <param name="payload">The encrypted payload.</param>
    public BffSessionState? Unprotect(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        try
        {
            return JsonSerializer.Deserialize<BffSessionState>(_protector.Unprotect(payload));
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return null;
        }
    }
}

namespace WoW.Two.Sdk.Beta.Be.Identity.Keys;

/// <summary>
/// Supplies the OIDC server's signing and encryption certificates. The default reads the settings, inline PEM bundles or
/// file paths (<see cref="SettingsSigningKeySource"/>); a database-held source can replace it without touching the server
/// wiring.
/// </summary>
/// <remarks>
///   - read once, when the server's options are built at boot; a rotation reaches the server with the next restart
///   - every certificate returned must carry its private key; <see cref="SigningKeyValidator"/> checks the set before use
/// </remarks>
public interface ISigningKeySource
{
    /// <summary>Load the certificate sets.</summary>
    /// <returns>The signing and encryption certificates, in any order.</returns>
    /// <exception cref="InvalidOperationException">A configured certificate is missing or unreadable.</exception>
    SigningKeySet Load();
}

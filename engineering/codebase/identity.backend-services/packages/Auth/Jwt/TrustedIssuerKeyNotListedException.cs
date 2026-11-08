using Microsoft.IdentityModel.Tokens;

namespace WoW.Two.Sdk.Beta.Be.Identity.Jwt;

/// <summary>
/// Refuses a token whose key a discovered issuer's keys don't list, even after a refresh: a rotation discovery can't show
/// yet, or a forged key id. An issuer with keys of its own never raises it, since nothing can change those.
/// </summary>
/// <param name="message">The validation failure's message.</param>
/// <param name="inner">The failure the token handler raised.</param>
internal sealed class TrustedIssuerKeyNotListedException(string message, Exception inner)
    : SecurityTokenSignatureKeyNotFoundException(message, inner);

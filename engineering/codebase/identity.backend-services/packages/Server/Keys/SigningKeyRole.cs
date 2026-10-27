namespace WoW.Two.Sdk.Beta.Be.Identity.Keys;

/// <summary>Refers to what an OIDC server certificate is for.</summary>
public enum SigningKeyRole
{
    /// <summary>Signs ID and access tokens (RS256); its public half is published in the JWKS.</summary>
    Signing,

    /// <summary>Encrypts authorization codes and refresh tokens, which only the server reads (RSA-OAEP).</summary>
    Encryption,
}

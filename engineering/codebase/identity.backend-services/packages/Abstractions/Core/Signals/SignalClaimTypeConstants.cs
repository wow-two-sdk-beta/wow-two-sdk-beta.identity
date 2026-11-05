namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;

/// <summary>Holds what a revocation signal's signature carries: a short JWT signed with the issuer's token key.</summary>
public static class SignalClaimTypeConstants
{
    /// <summary>The signature's <c>typ</c>, so it can't pass for an access token or the reverse.</summary>
    public const string TokenType = "webhook+jwt";

    /// <summary>The event type the signature covers (<c>event</c>).</summary>
    public const string Event = "event";

    /// <summary>The base64url SHA-256 of the body as sent (<c>body_sha256</c>).</summary>
    public const string BodyHash = "body_sha256";
}

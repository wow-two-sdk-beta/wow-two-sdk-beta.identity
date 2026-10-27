namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>
/// Holds the session claim: the id of the sign-in session a cookie, an ID token or an access token was issued under, so a
/// relying party can match a back-channel logout or a revocation to its own session.
/// </summary>
public static class SessionClaimTypeConstants
{
    /// <summary>The session id (<c>sid</c>, OpenID Connect Front-Channel and Back-Channel Logout): a uuid in its <c>D</c> form.</summary>
    public const string SessionId = "sid";
}

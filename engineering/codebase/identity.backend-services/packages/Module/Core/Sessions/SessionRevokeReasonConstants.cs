namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;

/// <summary>Names why a session ended early, in <c>revoke_reason</c>.</summary>
public static class SessionRevokeReasonConstants
{
    /// <summary>The person signed out of it, through the account API or the OIDC end-session endpoint.</summary>
    public const string SignOut = "sign_out";

    /// <summary>The person revoked it from the session list.</summary>
    public const string Revoked = "revoked";

    /// <summary>The person revoked all their sessions at once.</summary>
    public const string RevokedAll = "revoked_all";

    /// <summary>The person's security stamp rotated (a credential change or an explicit sign-out everywhere).</summary>
    public const string SecurityStamp = "security_stamp";
}

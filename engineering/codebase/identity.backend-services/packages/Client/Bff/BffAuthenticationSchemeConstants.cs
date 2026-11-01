namespace WoW.Two.Sdk.Beta.Be.Identity.Bff;

/// <summary>Holds the authentication schemes <c>AddIdentityBff</c> registers.</summary>
public static class BffAuthenticationSchemeConstants
{
    /// <summary>Holds the session cookie: the default scheme, carrying only the session reference.</summary>
    public const string Cookie = "Identity.Bff";

    /// <summary>Holds the OpenID Connect code flow against the instance; only <c>{prefix}/sign-in</c> challenges it.</summary>
    public const string OpenIdConnect = "Identity.Bff.Oidc";
}

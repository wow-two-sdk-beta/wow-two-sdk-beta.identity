namespace WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

/// <summary>Holds the paths the OpenID Connect server answers at; discovery advertises them.</summary>
public static class OidcServerPathConstants
{
    /// <summary>The authorization endpoint (pass-through: our session and claims).</summary>
    public const string Authorization = "/connect/authorize";

    /// <summary>The token endpoint (pass-through: re-checks the person and refreshes claims).</summary>
    public const string Token = "/connect/token";

    /// <summary>The userinfo endpoint (pass-through: current profile claims).</summary>
    public const string UserInfo = "/connect/userinfo";

    /// <summary>The end-session endpoint (pass-through: ends the session and the app's tokens).</summary>
    public const string EndSession = "/connect/endsession";

    /// <summary>The discovery document, served by OpenIddict.</summary>
    public const string Configuration = "/.well-known/openid-configuration";

    /// <summary>The JSON Web Key Set, served by OpenIddict.</summary>
    public const string JsonWebKeySet = "/.well-known/jwks";
}

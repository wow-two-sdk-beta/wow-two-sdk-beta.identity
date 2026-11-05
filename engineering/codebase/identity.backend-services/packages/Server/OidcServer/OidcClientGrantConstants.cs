namespace WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

/// <summary>Holds the values of <see cref="OidcClientOptions.Grants"/>.</summary>
public static class OidcClientGrantConstants
{
    /// <summary>People sign in to the app: the code flow with PKCE and its refresh tokens.</summary>
    public const string AuthorizationCode = "authorization_code";

    /// <summary>The app calls an API as itself: a token whose <c>sub</c> is the client id.</summary>
    public const string ClientCredentials = "client_credentials";

    /// <summary>The app calls an API for the person whose token it holds: the token keeps the person and adds <c>act</c>.</summary>
    public const string TokenExchange = "token_exchange";
}

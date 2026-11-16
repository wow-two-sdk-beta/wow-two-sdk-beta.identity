namespace WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

/// <summary>How a client's application runs and secures its token requests.</summary>
public enum OidcClientType
{
    /// <summary>A web application or service that authenticates with a secret or signing key.</summary>
    Web,

    /// <summary>An installed app that uses the authorization code flow with PKCE and has no client credential.</summary>
    Native,
}

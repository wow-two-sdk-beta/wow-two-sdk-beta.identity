namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>Holds the claims of a token between services, beside the person's own (RFC 9068, RFC 8693).</summary>
public static class ServiceClaimTypeConstants
{
    /// <summary>The calling app's client id (<c>client_id</c>, RFC 9068), on every access token.</summary>
    public const string ClientId = "client_id";

    /// <summary>The calling app under its OpenID Connect name (<c>azp</c>), which other issuers may send instead of <see cref="ClientId"/>.</summary>
    public const string AuthorizedParty = "azp";

    /// <summary>The granted scopes (<c>scope</c>): one space-delimited value on a token, one value per claim on an API key.</summary>
    public const string Scope = "scope";

    /// <summary>Who acts for the token's person (<c>act</c>, RFC 8693): a JSON object with a <c>sub</c>, on an exchanged token only.</summary>
    public const string Actor = "act";

    /// <summary>The tenant a trusted issuer or an API key stamped on the principal; never read from a token's own claims.</summary>
    public const string Partition = "partition";
}

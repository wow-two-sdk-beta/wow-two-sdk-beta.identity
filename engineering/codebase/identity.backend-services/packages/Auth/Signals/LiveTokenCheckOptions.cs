using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;

namespace WoW.Two.Sdk.Beta.Be.Identity.Signals;

/// <summary>
/// Holds how a service asks Identity whether a token is live: the introspection endpoint and the client asking, which authenticates
/// as at the token endpoint and needs <c>CanIntrospect</c> at the instance. A <c>private_key_jwt</c> assertion's audience defaults
/// to the endpoint's origin; set <see cref="OAuth2TokenClientOptions.ClientAssertionAudience"/> when the issuer has a path.
/// </summary>
public sealed record LiveTokenCheckOptions : OAuth2TokenClientOptions
{
    /// <summary>Gets or sets the absolute URL of <c>/connect/introspect</c>. Required; HTTPS unless loopback, as it carries the token.</summary>
    public Uri? IntrospectionEndpoint { get; set; }

    /// <summary>Gets or sets the handler introspection goes through, for a test host; null uses the network.</summary>
    public HttpMessageHandler? BackchannelHttpHandler { get; set; }
}

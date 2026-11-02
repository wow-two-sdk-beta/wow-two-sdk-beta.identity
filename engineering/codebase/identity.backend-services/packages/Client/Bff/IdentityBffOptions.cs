using WoW.Two.Sdk.Beta.Be.Identity.Cookies;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff;

/// <summary>
/// Holds how a BFF (an API and its SPA on one origin) signs in through an Identity instance: the instance and the client
/// registered there, the scopes, where the session routes live, which requests are API calls, and the session's limits.
/// </summary>
/// <remarks>
/// Bound from the host section <c>Identity:Client</c> first; the code configuration of <c>AddIdentityBff</c> runs after
/// it, so code wins. Validated at boot. Register the client at the instance with the redirect URI
/// <c>{origin}{RoutePrefix}/callback</c> and the post-sign-out URI <c>{origin}{PostSignOutRedirectPath}</c>.
/// </remarks>
public sealed record IdentityBffOptions
{
    /// <summary>Holds the host configuration section.</summary>
    public const string SectionName = "Identity:Client";

    /// <summary>Holds the scopes requested when <see cref="Scopes"/> is empty.</summary>
    public static readonly IReadOnlyList<string> DefaultScopes = ["openid", "profile", "email", "phone", "offline_access"];

    /// <summary>Holds the API path prefixes used when <see cref="ApiPathPrefixes"/> is empty.</summary>
    public static readonly IReadOnlyList<string> DefaultApiPathPrefixes = ["/api"];

    /// <summary>Gets or sets the instance's issuer, such as <c>https://id.example.com/</c>; discovery is read from
    /// <c>{Authority}/.well-known/openid-configuration</c>. Required; HTTPS unless <see cref="IsHttpsMetadataRequired"/> is off.</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>Gets or sets the client id registered at the instance. Required.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Gets or sets the client secret the BFF authenticates with at the token endpoint; a vault secret, never
    /// committed. Null for a public client.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Gets or sets the scopes to request; must hold <c>openid</c>, and <c>offline_access</c> for refresh. Empty
    /// requests <see cref="DefaultScopes"/>: <c>openid profile email phone offline_access</c>.</summary>
    public List<string> Scopes { get; set; } = [];

    /// <summary>Gets or sets where the session routes live: <c>{RoutePrefix}/session</c>, <c>/sign-in</c>,
    /// <c>/sign-out</c> and the code callback <c>/callback</c>. Default <c>/api/identity</c>; a path with a leading and no
    /// trailing slash. Under the default <c>/api</c> prefix the session routes stay API calls (<c>/sign-out</c> needs
    /// <c>X-Csrf-Token</c>), while <c>/sign-in</c> and <c>/callback</c> redirect by design.</summary>
    public string RoutePrefix { get; set; } = "/api/identity";

    /// <summary>Gets or sets the path prefixes of API requests: they answer 401 and 403 problems instead of a redirect,
    /// and their unsafe methods need the <c>X-Csrf-Token</c> header. Empty uses <see cref="DefaultApiPathPrefixes"/>
    /// (<c>/api</c>).</summary>
    public List<string> ApiPathPrefixes { get; set; } = [];

    /// <summary>Gets or sets the local path end-session returns the browser to. Default <c>/</c>.</summary>
    public string PostSignOutRedirectPath { get; set; } = "/";

    /// <summary>Gets or sets the session lifetime from the sign-in when the instance names no end of its own sign-in
    /// session (<c>refresh_token_expires_in</c>); a wow2 instance names it, and the session then ends with the instance's,
    /// fixed or sliding (<c>Identity:Sessions</c>). Default 12 hours.</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Gets or sets how long before the access token expires the session refreshes it. Default 1 minute.</summary>
    public TimeSpan AccessTokenRefreshMargin { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Gets or sets the session cookie's name; <see cref="Environment"/> suffixes it outside
    /// <see cref="PrimaryEnvironment"/>. Default <c>.app.session</c>.</summary>
    public string CookieName { get; set; } = ".app.session";

    /// <summary>Gets or sets the deploy environment, such as <c>dev</c>; null on a local run
    /// (<see cref="EnvironmentCookieName.For"/>).</summary>
    public string? Environment { get; set; }

    /// <summary>Gets or sets the environment that keeps the plain <see cref="CookieName"/>. Default <c>prod</c>.</summary>
    public string PrimaryEnvironment { get; set; } = EnvironmentCookieName.DefaultPrimaryEnvironment;

    /// <summary>Gets or sets whether discovery must come over HTTPS. Default <c>true</c>; switch it off only for a
    /// loopback instance on a local run.</summary>
    public bool IsHttpsMetadataRequired { get; set; } = true;

    /// <summary>Gets the scopes to request: <see cref="Scopes"/>, or <see cref="DefaultScopes"/> when empty.</summary>
    public IReadOnlyList<string> EffectiveScopes => Scopes.Count > 0 ? Scopes : DefaultScopes;

    /// <summary>Gets the API path prefixes: <see cref="ApiPathPrefixes"/>, or <see cref="DefaultApiPathPrefixes"/> when empty.</summary>
    public IReadOnlyList<string> EffectiveApiPathPrefixes => ApiPathPrefixes.Count > 0 ? ApiPathPrefixes : DefaultApiPathPrefixes;
}

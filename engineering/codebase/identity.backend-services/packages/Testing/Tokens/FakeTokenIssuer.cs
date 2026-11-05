using System.Globalization;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Jwt;

namespace WoW.Two.Sdk.Beta.Be.Identity.Testing;

/// <summary>
/// Signs the access tokens Identity issues, in their exact shape, so a consumer tests against them with no Identity
/// running. Trust it with <see cref="FakeTokenIssuerServiceCollectionExtensions.AddFakeTokenIssuer"/> or
/// <see cref="AsTrustedIssuer"/>; its key lives in memory, one per instance.
/// </summary>
public sealed class FakeTokenIssuer : IDisposable
{
    private const string AccessTokenType = "at+jwt";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly SigningCredentials _credentials;
    private readonly JsonWebTokenHandler _tokens = new() { SetDefaultTimesOnTokenCreation = false };
    private bool _isDisposed;

    /// <summary>Initializes a new instance of the <see cref="FakeTokenIssuer"/> class.</summary>
    /// <param name="issuer">The token's <c>iss</c>.</param>
    /// <param name="clock">The clock behind <c>iat</c>, <c>nbf</c> and <c>exp</c>; null is the system clock.</param>
    public FakeTokenIssuer(string issuer = "https://fake-issuer.test/", TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        Issuer = issuer;
        Clock = clock ?? TimeProvider.System;

        var keyId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        _credentials = new SigningCredentials(new RsaSecurityKey(_rsa) { KeyId = keyId }, SecurityAlgorithms.RsaSha256);
        VerificationKey = new RsaSecurityKey(_rsa.ExportParameters(includePrivateParameters: false)) { KeyId = keyId };
    }

    /// <summary>Gets the issuer every token names as <c>iss</c>.</summary>
    public string Issuer { get; }

    /// <summary>Gets or sets how long a token lives. Default 5 minutes, as Identity's <c>ServiceTokenLifetime</c>.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets the scopes the token endpoint grants per resource: every token for a resource lists them, whatever was asked, as
    /// Identity's tokens list every scope the client may use there. A resource not named here gets the scopes asked for.
    /// </summary>
    public IDictionary<string, string[]> ResourceScopes { get; } = new Dictionary<string, string[]>(StringComparer.Ordinal);

    /// <summary>Gets the public half of the signing key.</summary>
    internal RsaSecurityKey VerificationKey { get; }

    /// <summary>Gets the clock tokens are dated by.</summary>
    internal TimeProvider Clock { get; }

    /// <summary>Describes this issuer for <see cref="JwtOptions.TrustedIssuers"/>: a static key, so no discovery.</summary>
    /// <param name="partition">The tenant stamped on the principal; null stamps none.</param>
    /// <param name="audiences">The audiences its tokens may name; none means those of <see cref="JwtOptions"/>.</param>
    /// <returns>The trusted issuer.</returns>
    public TrustedIssuer AsTrustedIssuer(string? partition = null, params string[] audiences) => new()
    {
        Issuer = Issuer,
        SigningKeys = [VerificationKey],
        Audiences = audiences,
        Partition = partition,
    };

    /// <summary>Signs a client credentials token: <c>sub</c> and <c>client_id</c> are the client.</summary>
    /// <param name="clientId">The calling app.</param>
    /// <param name="audience">The token's only <c>aud</c>.</param>
    /// <param name="scopes">The granted scopes.</param>
    /// <returns>The signed token.</returns>
    public string IssueServiceToken(string clientId, string audience, params string[] scopes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        return Sign(clientId, clientId, audience, scopes, new Dictionary<string, object>(StringComparer.Ordinal));
    }

    /// <summary>Signs a person's own access token.</summary>
    /// <param name="person">The person.</param>
    /// <param name="audience">The token's only <c>aud</c>.</param>
    /// <param name="clientId">The app the token was issued to.</param>
    /// <param name="scopes">The granted scopes.</param>
    /// <returns>The signed token.</returns>
    public string IssuePersonToken(FakePerson person, string audience, string clientId = "fake-app", params string[] scopes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        return Sign(Subject(person), clientId, audience, scopes, PersonClaims(person));
    }

    /// <summary>Signs a token exchange result: the person's claims, plus <c>act</c> naming the app that acts for them.</summary>
    /// <param name="person">The person.</param>
    /// <param name="actingClientId">The acting app, also the token's <c>client_id</c>.</param>
    /// <param name="audience">The token's only <c>aud</c>.</param>
    /// <param name="scopes">The granted scopes.</param>
    /// <returns>The signed token.</returns>
    public string IssueExchangedToken(FakePerson person, string actingClientId, string audience, params string[] scopes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actingClientId);
        var claims = PersonClaims(person);
        claims[ServiceClaimTypeConstants.Actor] = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [JwtRegisteredClaimNames.Sub] = actingClientId,
        };
        return Sign(Subject(person), actingClientId, audience, scopes, claims);
    }

    /// <summary>
    /// Creates a handler that answers form posts at any address as Identity's token endpoint does, for
    /// <c>client_credentials</c> and token exchange, with
    /// <c>{ access_token, token_type, expires_in[, issued_token_type] }</c> or, for a failure,
    /// <c>{ error, error_description }</c> and <c>400</c>.
    /// </summary>
    /// <remarks>
    /// It has no registry beyond <see cref="ResourceScopes"/>: any client may ask for any resource and scope, its secret or
    /// assertion isn't checked, and an exchanged token keeps every permission. An exchange checks what doesn't need one:
    /// the subject token is this issuer's and live, a person's own, and held by its presenter or audience.
    /// </remarks>
    /// <returns>The handler, for an <see cref="HttpClient"/> or a named client's primary handler.</returns>
    public HttpMessageHandler CreateTokenEndpointHandler() => new FakeTokenEndpointHandler(this);

    /// <inheritdoc />
    public void Dispose()
    {
        _isDisposed = true;
        _rsa.Dispose();
    }

    private static string Subject(FakePerson person)
    {
        ArgumentNullException.ThrowIfNull(person);
        return person.Id.ToString("D", CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, object> PersonClaims(FakePerson person)
    {
        ArgumentNullException.ThrowIfNull(person);
        var claims = new Dictionary<string, object>(StringComparer.Ordinal);
        if (person.SessionId is { } session)
            claims[SessionClaimTypeConstants.SessionId] = session.ToString("D", CultureInfo.InvariantCulture);

        if (person.OrganizationId is not { } id)
        {
            if (person.Role is not null || person.Permissions.Count > 0)
                throw new ArgumentException("A role or permissions need an organization.", nameof(person));

            return claims;
        }

        var organization = id.ToString("D", CultureInfo.InvariantCulture);
        claims[OrganizationClaimTypeConstants.Id] = organization;
        if (person.Role is { } role)
            claims[OrganizationClaimTypeConstants.Role] = $"{organization}:{role}";
        if (person.Permissions.Count > 0)
        {
            claims[OrganizationClaimTypeConstants.Permission] =
                person.Permissions.Select(permission => $"{organization}:{permission}").ToArray();
        }

        return claims;
    }

    private string Sign(
        string subject,
        string clientId,
        string audience,
        string[] scopes,
        Dictionary<string, object> claims)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentNullException.ThrowIfNull(scopes);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        claims[JwtRegisteredClaimNames.Sub] = subject;
        claims[ServiceClaimTypeConstants.ClientId] = clientId;
        claims[JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        if (scopes.Length > 0)
            claims[ServiceClaimTypeConstants.Scope] = string.Join(' ', scopes);

        var now = Clock.GetUtcNow().UtcDateTime;
        return _tokens.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + Lifetime,
            TokenType = AccessTokenType,
            SigningCredentials = _credentials,
            Claims = claims,
        });
    }
}

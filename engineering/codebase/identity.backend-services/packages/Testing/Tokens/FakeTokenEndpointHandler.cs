using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

namespace WoW.Two.Sdk.Beta.Be.Identity.Testing;

/// <summary>
/// Answers token requests as Identity's <c>/connect/token</c> does, from a <see cref="FakeTokenIssuer"/>; see
/// <see cref="FakeTokenIssuer.CreateTokenEndpointHandler"/>.
/// </summary>
internal sealed class FakeTokenEndpointHandler(FakeTokenIssuer issuer) : HttpMessageHandler
{
    private const string ClientCredentialsGrant = "client_credentials";
    private const string TokenExchangeGrant = "urn:ietf:params:oauth:grant-type:token-exchange";
    private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    private readonly JsonWebTokenHandler _tokens = new() { MapInboundClaims = false };

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Method != HttpMethod.Post || request.Content is null)
            return Failure("invalid_request", "The token endpoint takes a form post.");

        using var reader = new FormReader(await request.Content.ReadAsStringAsync(cancellationToken));
        var form = await reader.ReadFormAsync(cancellationToken);
        var grant = FormValue(form, "grant_type");
        if (grant is not (ClientCredentialsGrant or TokenExchangeGrant))
            return Failure("unsupported_grant_type", "The fake issues client credentials and token exchange only.");

        if (FormValue(form, "client_id") is not { Length: > 0 } clientId)
            return Failure("invalid_client", "Name the client: client_id.");

        if (!form.TryGetValue("resource", out var resources) || resources is not [{ Length: > 0 } resource])
            return Failure("invalid_target", "Name exactly one resource: a service token has one audience.");

        var scopes = issuer.ResourceScopes.TryGetValue(resource, out var listed)
            ? listed
            : FormValue(form, "scope")?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        return grant == ClientCredentialsGrant
            ? Success(issuer.IssueServiceToken(clientId, resource, scopes))
            : await ExchangeAsync(form, clientId, resource, scopes);
    }

    private async Task<HttpResponseMessage> ExchangeAsync(
        Dictionary<string, StringValues> form,
        string clientId,
        string resource,
        string[] scopes)
    {
        if (form.ContainsKey("actor_token"))
            return Failure("invalid_request", "actor_token is not supported.");
        if (FormValue(form, "requested_token_type") is { } requested && requested != AccessTokenType)
            return Failure("invalid_request", "Only an access token can be requested.");
        if (FormValue(form, "subject_token_type") != AccessTokenType)
            return Failure("invalid_request", "subject_token_type must be an access token.");
        if (FormValue(form, "subject_token") is not { Length: > 0 } subjectToken)
            return Failure("invalid_request", "Name the subject_token.");

        // Lifetime against the issuer's own clock, which a test may have set anywhere.
        var validation = await _tokens.ValidateTokenAsync(subjectToken, new TokenValidationParameters
        {
            ValidIssuer = issuer.Issuer,
            IssuerSigningKey = issuer.VerificationKey,
            ValidateAudience = false,
            ValidateLifetime = false,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        });
        if (!validation.IsValid
            || validation.SecurityToken is not JsonWebToken subject
            || subject.ValidTo <= issuer.Clock.GetUtcNow().UtcDateTime)
        {
            return Failure("invalid_grant", "The subject token is invalid or expired.");
        }

        if (subject.TryGetPayloadValue<object>(ServiceClaimTypeConstants.Actor, out _))
            return Failure("invalid_grant", "Only a person's own token can be exchanged.");

        var presenter = subject.TryGetPayloadValue<string>(ServiceClaimTypeConstants.ClientId, out var value) ? value : null;
        if (presenter != clientId && !subject.Audiences.Contains(clientId, StringComparer.Ordinal))
            return Failure("invalid_grant", "The client is neither the subject token's presenter nor its audience.");

        return ReadPerson(subject) is { } person
            ? Success(issuer.IssueExchangedToken(person, clientId, resource, scopes), AccessTokenType)
            : Failure("invalid_grant", "The subject token names no person.");
    }

    /// <summary>The person a token names; a service token's <c>sub</c>, its client id, names none.</summary>
    private static FakePerson? ReadPerson(JsonWebToken token)
    {
        if (!Guid.TryParse(token.Subject, out var id))
            return null;

        var organization = Value(token, OrganizationClaimTypeConstants.Id);
        var prefix = organization + ":";
        return new FakePerson
        {
            Id = id,
            OrganizationId = organization is null ? null : Guid.Parse(organization),
            Role = Value(token, OrganizationClaimTypeConstants.Role)?[prefix.Length..],
            Permissions = token.Claims
                .Where(claim => claim.Type == OrganizationClaimTypeConstants.Permission)
                .Select(claim => claim.Value[prefix.Length..])
                .ToArray(),
            SessionId = Value(token, SessionClaimTypeConstants.SessionId) is { } session ? Guid.Parse(session) : null,
        };
    }

    private static string? Value(JsonWebToken token, string type) =>
        token.TryGetClaim(type, out var claim) ? claim.Value : null;

    private static string? FormValue(Dictionary<string, StringValues> form, string name) =>
        form.TryGetValue(name, out var values) ? values.ToString() : null;

    private HttpResponseMessage Success(string token, string? issuedTokenType = null)
    {
        var body = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["access_token"] = token,
            ["token_type"] = "Bearer",
            ["expires_in"] = (long)issuer.Lifetime.TotalSeconds,
        };
        if (issuedTokenType is not null)
            body["issued_token_type"] = issuedTokenType;

        return Json(HttpStatusCode.OK, body);
    }

    private static HttpResponseMessage Failure(string error, string description) =>
        Json(HttpStatusCode.BadRequest, new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["error"] = error,
            ["error_description"] = description,
        });

    private static HttpResponseMessage Json(HttpStatusCode status, Dictionary<string, object> body) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
}

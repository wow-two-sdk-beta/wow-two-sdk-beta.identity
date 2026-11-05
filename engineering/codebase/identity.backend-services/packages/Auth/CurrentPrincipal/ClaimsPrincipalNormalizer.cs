using System.Security.Claims;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

namespace WoW.Two.Sdk.Beta.Be.Identity.CurrentPrincipal;

/// <summary>
/// The one place that reads a caller out of a <see cref="ClaimsPrincipal"/>: a JWT, a cookie session and an API key all
/// come out as the same <see cref="NormalizedPrincipal"/>, so a policy never branches by scheme.
/// </summary>
public static class ClaimsPrincipalNormalizer
{
    /// <summary>The name some issuers use for <see cref="ServiceClaimTypeConstants.Scope"/>.</summary>
    private const string ScpClaimType = "scp";

    /// <summary>
    /// Normalizes <paramref name="principal"/>. A token whose <c>sub</c> is its own client id is a service (RFC 9068); an
    /// identity the API key scheme authenticated is a key; any other authenticated identity is a person. Roles and
    /// permissions lose their <c>{org}:</c> prefix under an active organization and drop other organizations' values; with
    /// no active organization, as on a cookie carrying every membership, they keep it.
    /// </summary>
    /// <param name="principal">The authenticated user of the request.</param>
    /// <param name="subjectClaimType">The claim a cookie session names its account by; <c>sub</c> is read first.</param>
    /// <returns>The caller; <see cref="NormalizedPrincipal.Anonymous"/> when nobody is authenticated.</returns>
    public static NormalizedPrincipal Normalize(ClaimsPrincipal principal, string subjectClaimType = ClaimTypes.NameIdentifier)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (principal.Identities.FirstOrDefault(identity => identity.IsAuthenticated) is not { } identity)
            return NormalizedPrincipal.Anonymous;

        var isApiKey = identity.AuthenticationType == ApiKeyAuthenticationDefaults.Scheme;
        var subject = First(identity, JwtRegisteredClaimNames.Sub)
            ?? (isApiKey ? First(identity, ApiKeyAuthenticationDefaults.KeyIdClaim) : First(identity, subjectClaimType));
        var clientId = First(identity, ServiceClaimTypeConstants.ClientId) ?? First(identity, ServiceClaimTypeConstants.AuthorizedParty);
        var organization = First(identity, OrganizationClaimTypeConstants.Id);
        return new NormalizedPrincipal
        {
            Subject = subject,
            Kind = isApiKey ? PrincipalKind.ApiKey
                : clientId is not null && string.Equals(subject, clientId, StringComparison.Ordinal) ? PrincipalKind.Service
                : PrincipalKind.Person,
            Issuer = First(identity, JwtRegisteredClaimNames.Iss),
            ClientId = clientId,
            OrganizationId = organization,
            Roles = OrganizationValues(identity, OrganizationClaimTypeConstants.Role, organization),
            Permissions = OrganizationValues(identity, OrganizationClaimTypeConstants.Permission, organization),
            Scopes = identity.Claims
                .Where(claim => claim.Type is ServiceClaimTypeConstants.Scope or ScpClaimType)
                .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .ToHashSet(StringComparer.Ordinal),
            Actor = First(identity, ServiceClaimTypeConstants.Actor) is { } actor ? ReadActor(actor) : null,
            Partition = First(identity, ServiceClaimTypeConstants.Partition),
        };
    }

    private static string? First(ClaimsIdentity identity, string type) => identity.FindFirst(type)?.Value;

    private static HashSet<string> OrganizationValues(ClaimsIdentity identity, string type, string? organization)
    {
        var values = identity.FindAll(type).Select(claim => claim.Value);
        if (organization is null)
            return values.ToHashSet(StringComparer.Ordinal);

        var prefix = organization + ":";
        return values
            .Where(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(value => value[prefix.Length..])
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Reads an <c>act</c> claim and any chain inside it; a value that isn't the expected object still says someone acts.</summary>
    private static ActorInfo ReadActor(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return ReadActor(document.RootElement) ?? new ActorInfo { Subject = value };
        }
        catch (JsonException)
        {
            return new ActorInfo { Subject = value };
        }
    }

    private static ActorInfo? ReadActor(JsonElement element)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(JwtRegisteredClaimNames.Sub, out var subject)
           && subject.ValueKind == JsonValueKind.String
            ? new ActorInfo
            {
                Subject = subject.GetString()!,
                Actor = element.TryGetProperty(ServiceClaimTypeConstants.Actor, out var next) ? ReadActor(next) : null,
            }
            : null;
}

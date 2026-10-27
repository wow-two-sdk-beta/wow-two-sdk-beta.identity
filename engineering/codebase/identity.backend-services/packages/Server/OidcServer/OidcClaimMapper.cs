using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

/// <summary>
/// The one place that turns a person into token claims and sends each claim to its tokens: <c>sub</c>, <c>sid</c>,
/// <c>name</c>, <c>email</c>, <c>email_verified</c>, <c>amr</c>, <c>auth_time</c>, and <c>org_id</c>, <c>org_role</c>,
/// <c>org_permission</c> for one active organization.
/// </summary>
/// <remarks>
///   - the active organization is the one the token already carries while the person still belongs to it, else the
///     earliest membership (ties by organization id); a person with one membership always gets that one
///   - <c>org_permission</c> keeps only the client's namespace when it has one
///   - <c>amr</c> and <c>org_permission</c> travel as JSON arrays, one value included, as OIDC defines <c>amr</c>
///   - the security stamp rides inside codes and refresh tokens only, so the token endpoint refuses a rotated stamp
///   - <c>sid</c> names the sign-in session the tokens were issued under; it is set once, at authorization, and a refresh
///     keeps it
/// </remarks>
/// <param name="core">Identity options carrying the stamp claim type.</param>
/// <param name="organizations">The organizations slice; null emits no organization claims.</param>
internal sealed class OidcClaimMapper(IdentityCoreOptions core, OrganizationService? organizations = null)
{
    /// <summary>The claim types this mapper owns; a refresh replaces every one of them.</summary>
    private static readonly string[] PersonClaimTypes =
    [
        Claims.Subject, Claims.Name, Claims.Email, Claims.EmailVerified,
        OrganizationClaimTypeConstants.Id, OrganizationClaimTypeConstants.Role, OrganizationClaimTypeConstants.Permission,
    ];

    /// <summary>A new identity for a sign-in, carrying the granted scopes, the client's audience, the session and how the person signed in.</summary>
    /// <param name="client">The client.</param>
    /// <param name="scopes">The granted scopes.</param>
    /// <param name="methods">The <c>amr</c> values of the session.</param>
    /// <param name="authenticatedAt">When the session signed in; null omits <c>auth_time</c>.</param>
    /// <param name="sessionId">The sign-in session the tokens are issued under; null omits <c>sid</c>.</param>
    public static ClaimsIdentity CreateIdentity(
        OidcClientProfile client,
        IEnumerable<string> scopes,
        IEnumerable<string> methods,
        DateTimeOffset? authenticatedAt,
        Guid? sessionId = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetScopes(scopes);
        identity.SetAudiences(client.Audience);
        SetArrayClaim(identity, Claims.AuthenticationMethodReference, methods);
        if (sessionId is { } sid)
            identity.SetClaim(SessionClaimTypeConstants.SessionId, sid.ToString("D", CultureInfo.InvariantCulture));
        // The typed overloads set the value type a token needs: OpenIddict's string overload takes an issuer, not a type.
        if (authenticatedAt is { } at)
            identity.SetClaim(Claims.AuthenticationTime, at.ToUnixTimeSeconds());

        return identity;
    }

    /// <summary>
    /// Write <paramref name="user"/>'s current claims into <paramref name="identity"/>, replacing any earlier ones, and set
    /// every claim's destinations. A refresh passes the identity from the refresh token, keeping its scopes, audience,
    /// methods and authorization.
    /// </summary>
    /// <param name="identity">The identity to fill.</param>
    /// <param name="user">The person.</param>
    /// <param name="client">The client the tokens are for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ClaimsPrincipal> ApplyAsync(ClaimsIdentity identity, IdentityUser user, OidcClientProfile client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(client);

        var preferred = Guid.TryParse(identity.GetClaim(OrganizationClaimTypeConstants.Id), out var id) ? id : (Guid?)null;
        SetPersonClaims(identity, user, core.Claims.SecurityStampClaimType);

        // A token read back holds an array as one claim per value; one array claim again keeps it an array.
        SetArrayClaim(identity, Claims.AuthenticationMethodReference, ReadValues(identity, Claims.AuthenticationMethodReference));
        if (organizations is not null)
            SetOrganizationClaims(identity, await organizations.ListForUserAsync(user, cancellationToken), organizations.GetPermissions, client.PermissionNamespace, preferred);

        identity.SetDestinations(GetDestinations);
        return new ClaimsPrincipal(identity);
    }

    /// <summary>Write the person's own claims: id, name, email and its verification, and the security stamp.</summary>
    /// <param name="identity">The identity.</param>
    /// <param name="user">The person.</param>
    /// <param name="securityStampClaimType">The claim type the stamp travels under.</param>
    internal static void SetPersonClaims(ClaimsIdentity identity, IdentityUser user, string securityStampClaimType)
    {
        foreach (var type in PersonClaimTypes.Append(securityStampClaimType))
            identity.RemoveClaims(type);

        identity.SetClaim(Claims.Subject, user.Id.ToString("D", CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(user.UserName))
            identity.SetClaim(Claims.Name, user.UserName);
        if (!string.IsNullOrEmpty(user.Email))
        {
            identity.SetClaim(Claims.Email, user.Email);
            identity.SetClaim(Claims.EmailVerified, user.EmailConfirmed);
        }

        if (!string.IsNullOrEmpty(user.SecurityStamp))
            identity.SetClaim(securityStampClaimType, user.SecurityStamp);
    }

    /// <summary>
    /// Write the active organization's claims: <c>org_id</c>, <c>org_role</c> as <c>{org}:{role}</c> and one
    /// <c>org_permission</c> per permission as <c>{org}:{permission}</c>, kept to <paramref name="permissionNamespace"/>.
    /// </summary>
    /// <param name="identity">The identity.</param>
    /// <param name="memberships">The person's memberships.</param>
    /// <param name="permissionsOf">The permissions a role grants.</param>
    /// <param name="permissionNamespace">The client's namespace; null keeps every permission.</param>
    /// <param name="preferred">The organization the token already carries, kept while the person still belongs to it.</param>
    /// <returns>The active organization, or null for a person without memberships.</returns>
    internal static Guid? SetOrganizationClaims(
        ClaimsIdentity identity,
        IReadOnlyList<OrganizationMembershipModel> memberships,
        Func<string, IReadOnlyList<string>> permissionsOf,
        string? permissionNamespace,
        Guid? preferred)
    {
        foreach (var type in new[] { OrganizationClaimTypeConstants.Id, OrganizationClaimTypeConstants.Role, OrganizationClaimTypeConstants.Permission })
            identity.RemoveClaims(type);

        var ordered = memberships.OrderBy(m => m.JoinedAt).ThenBy(m => m.OrganizationId).ToList();
        if ((ordered.FirstOrDefault(m => m.OrganizationId == preferred) ?? ordered.FirstOrDefault()) is not { } active)
            return null;

        var organization = active.OrganizationId.ToString("D", CultureInfo.InvariantCulture);
        var prefix = permissionNamespace is null ? null : permissionNamespace + ":";
        identity.SetClaim(OrganizationClaimTypeConstants.Id, organization);
        identity.SetClaim(OrganizationClaimTypeConstants.Role, $"{organization}:{active.Role}");
        SetArrayClaim(identity, OrganizationClaimTypeConstants.Permission, permissionsOf(active.Role)
            .Where(permission => prefix is null || permission.StartsWith(prefix, StringComparison.Ordinal))
            .Select(permission => $"{organization}:{permission}"));
        return active.OrganizationId;
    }

    /// <summary>The values of <paramref name="type"/>, whether held as one JSON array claim or as one claim per value.</summary>
    /// <param name="identity">The identity.</param>
    /// <param name="type">The claim type.</param>
    internal static IReadOnlyList<string> ReadValues(ClaimsIdentity identity, string type)
    {
        var values = new List<string>();
        foreach (var claim in identity.FindAll(type))
        {
            if (claim.ValueType == JsonClaimValueTypes.JsonArray)
                values.AddRange(JsonSerializer.Deserialize<string[]>(claim.Value) ?? []);
            else
                values.Add(claim.Value);
        }

        return values;
    }

    /// <summary>Replace <paramref name="type"/> with one JSON array claim of the distinct <paramref name="values"/>; none removes it.</summary>
    /// <param name="identity">The identity.</param>
    /// <param name="type">The claim type.</param>
    /// <param name="values">The values.</param>
    internal static void SetArrayClaim(ClaimsIdentity identity, string type, IEnumerable<string> values)
    {
        var distinct = values.Distinct(StringComparer.Ordinal).ToArray();
        identity.RemoveClaims(type);
        if (distinct.Length > 0)
            identity.AddClaim(new Claim(type, JsonSerializer.Serialize(distinct), JsonClaimValueTypes.JsonArray));
    }

    /// <summary>
    /// Where a claim goes. <c>sub</c>, <c>sid</c>, <c>amr</c> and the organization claims reach both tokens; <c>name</c> follows the
    /// <c>profile</c> scope and <c>email</c> with <c>email_verified</c> the <c>email</c> scope; <c>auth_time</c> reaches the
    /// ID token only. Everything else, the security stamp included, stays inside codes and refresh tokens.
    /// </summary>
    /// <param name="claim">The claim, whose identity carries the granted scopes.</param>
    public static IEnumerable<string> GetDestinations(Claim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        string[] both = [Destinations.AccessToken, Destinations.IdentityToken];
        return claim.Type switch
        {
            Claims.Subject or SessionClaimTypeConstants.SessionId or Claims.AuthenticationMethodReference => both,
            OrganizationClaimTypeConstants.Id or OrganizationClaimTypeConstants.Role or OrganizationClaimTypeConstants.Permission => both,
            Claims.Name => claim.Subject?.HasScope(Scopes.Profile) == true ? both : [],
            Claims.Email or Claims.EmailVerified => claim.Subject?.HasScope(Scopes.Email) == true ? both : [],
            Claims.AuthenticationTime => [Destinations.IdentityToken],
            _ => [],
        };
    }

    /// <summary>
    /// The userinfo response for <paramref name="principal"/>: the profile and organization claims the ID token may carry,
    /// <c>email_verified</c> as a boolean and <c>org_permission</c> always as an array; <c>amr</c>, <c>auth_time</c> and <c>sid</c> stay out.
    /// </summary>
    /// <param name="principal">A principal built by <see cref="ApplyAsync"/>.</param>
    public static Dictionary<string, object> ToUserInfo(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var identity = principal.Identity as ClaimsIdentity ?? new ClaimsIdentity(principal.Claims);
        var response = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var type in identity.Claims
                     .Where(claim => claim.Type is not (Claims.AuthenticationMethodReference or Claims.AuthenticationTime or SessionClaimTypeConstants.SessionId))
                     .Where(claim => GetDestinations(claim).Contains(Destinations.IdentityToken))
                     .Select(claim => claim.Type)
                     .Distinct(StringComparer.Ordinal))
        {
            var values = ReadValues(identity, type);
            response[type] = type switch
            {
                Claims.EmailVerified => bool.Parse(values[0]),
                OrganizationClaimTypeConstants.Permission => values,
                _ when values.Count > 1 => values,
                _ => values[0],
            };
        }

        return response;
    }
}

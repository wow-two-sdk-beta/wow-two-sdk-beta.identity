using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

/// <summary>
/// Issues the tokens one service sends another: client credentials for the app itself, token exchange for the person it
/// acts for. Every such token names one resource as its <c>aud</c>, lists every scope the client may use there and has no
/// refresh token.
/// </summary>
/// <remarks>
/// OpenIddict has already authenticated the client, checked the grant, resource and scope permissions and refused a
/// resource or scope no one registered. It never turns <c>resource</c> into <c>aud</c>: <c>aud</c> comes from the
/// principal, so the registry decides it here, along with which grant reaches which resource.
/// </remarks>
internal static class OidcServiceTokenGrants
{
    /// <summary>Issue the client's own token: <c>sub</c> is the client id; <c>org_id</c> only for a client bound to one organization.</summary>
    /// <param name="http">The token request.</param>
    /// <param name="request">The validated OAuth request.</param>
    /// <param name="client">The authenticated client.</param>
    public static IResult ClientCredentials(HttpContext http, OpenIddictRequest request, OidcClientProfile client)
    {
        var options = http.RequestServices.GetRequiredService<IOptions<OidcServerOptions>>().Value;
        if (RefuseOrSelect(request, client.ServiceAccess.CredentialResources, client, options, out var resource, out var scopes) is { } refusal)
            return refusal;

        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetScopes(scopes);
        identity.SetAudiences(resource.Uri);
        identity.SetClaim(Claims.Subject, client.ClientId);
        if (client.ServiceAccess.OrganizationId is { } organization)
            identity.SetClaim(OrganizationClaimTypeConstants.Id, organization.ToString("D", CultureInfo.InvariantCulture));

        identity.SetDestinations(OidcClaimMapper.GetDestinations);
        var principal = new ClaimsPrincipal(identity);
        principal.SetAccessTokenLifetime(options.ServiceTokenLifetime);
        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Issue a token for the person of the subject token (RFC 8693): it keeps their <c>sub</c>, <c>sid</c>, <c>amr</c>, active
    /// organization, role and permissions, written fresh for the target resource's namespace, and adds
    /// <c>act: { sub: client id }</c>. Only a person's own token passes, while their sign-in session lives, and the result
    /// never outlives that session.
    /// </summary>
    /// <param name="http">The token request.</param>
    /// <param name="request">The validated OAuth request.</param>
    /// <param name="client">The authenticated client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<IResult> TokenExchangeAsync(HttpContext http, OpenIddictRequest request, OidcClientProfile client, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var options = services.GetRequiredService<IOptions<OidcServerOptions>>().Value;
        if (RefuseOrSelect(request, client.ServiceAccess.ExchangeResources, client, options, out var resource, out var scopes) is { } refusal)
            return refusal;

        var subject = (await http.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;
        if (subject is null || subject.HasClaim(claim => claim.Type == ServiceClaimTypeConstants.Actor))
            return OidcServerEndpointRouteBuilderExtensions.Forbid(Errors.InvalidGrant, "Only a person's own token can be exchanged.");

        var person = await OidcServerEndpointRouteBuilderExtensions.FindPersonAsync(services, subject, sessionClaims: false, cancellationToken);
        if (person is null)
            return OidcServerEndpointRouteBuilderExtensions.Forbid(Errors.InvalidGrant, "The subject token names no person.");

        var (isLive, session) = await OidcServerEndpointRouteBuilderExtensions.SessionOfAsync(services, subject, person, cancellationToken);
        if (!isLive || (session is null && services.GetService<UserSessionService>() is not null))
            return OidcServerEndpointRouteBuilderExtensions.Forbid(Errors.InvalidGrant, "The sign-in session behind the subject token has ended.");

        var profile = client with { Audience = resource.Uri, PermissionNamespace = resource.PermissionNamespace };
        var claimsOfSubject = subject.Identity as ClaimsIdentity ?? new ClaimsIdentity(subject.Claims);
        var identity = OidcClaimMapper.CreateIdentity(profile, scopes, OidcClaimMapper.ReadValues(claimsOfSubject, Claims.AuthenticationMethodReference), authenticatedAt: null, session?.Id);
        identity.AddClaim(new Claim(ServiceClaimTypeConstants.Actor, JsonSerializer.Serialize(new { sub = client.ClientId }), JsonClaimValueTypes.Json));
        if (subject.GetClaim(OrganizationClaimTypeConstants.Id) is { } organization)
            identity.SetClaim(OrganizationClaimTypeConstants.Id, organization);

        var principal = await services.GetRequiredService<OidcClaimMapper>().ApplyAsync(identity, person, profile, cancellationToken);
        var now = (services.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
        principal.SetAccessTokenLifetime(session is { ExpiresAt: { } end } && end - now < options.ServiceTokenLifetime ? end - now : options.ServiceTokenLifetime);
        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Picks the one resource the token is for and its scopes: always every scope the client may use on it, so a caller holds
    /// one token per target and never one per action (developer, 2026-11-05). Answers the refusal for a request naming no
    /// resource or several, a resource this grant doesn't reach and a scope outside that set, so a token never holds a scope
    /// its own audience doesn't understand.
    /// </summary>
    private static IResult? RefuseOrSelect(
        OpenIddictRequest request,
        IReadOnlyList<string> reachable,
        OidcClientProfile client,
        OidcServerOptions options,
        out OidcResourceOptions resource,
        out IReadOnlyList<string> scopes)
    {
        resource = null!;
        scopes = [];
        if (request.GetResources() is not [var uri])
            return OidcServerEndpointRouteBuilderExtensions.Forbid(Errors.InvalidTarget, "Name exactly one resource: a service token has one audience.");

        if (!reachable.Contains(uri, StringComparer.Ordinal)
            || options.Resources.FirstOrDefault(candidate => string.Equals(candidate.Uri, uri, StringComparison.Ordinal)) is not { } registered)
            return OidcServerEndpointRouteBuilderExtensions.Forbid(Errors.InvalidTarget, "This client may not reach that resource with this grant.");

        var allowed = registered.Scopes.Intersect(client.ServiceAccess.Scopes, StringComparer.Ordinal).ToList();
        var asked = request.GetScopes();
        if (asked.Any(scope => !allowed.Contains(scope, StringComparer.Ordinal)) || (asked.IsEmpty && allowed.Count == 0))
            return OidcServerEndpointRouteBuilderExtensions.Forbid(Errors.InvalidScope, "This client may not ask for that scope on that resource.");

        resource = registered;
        scopes = allowed;
        return null;
    }
}

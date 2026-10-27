using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using OpenIddict.Abstractions;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

/// <summary>
/// Ties OIDC grants to the sign-in session they came from, and ends them with it: each authorization request creates an
/// ad-hoc authorization whose <see cref="SessionProperty"/> names the session, and revoking the session revokes those
/// authorizations and every token issued under them, refresh chains included.
/// </summary>
/// <param name="authorizations">OpenIddict's authorization manager.</param>
/// <param name="tokens">OpenIddict's token manager.</param>
internal sealed class OidcSessionRevocationHandler(IOpenIddictAuthorizationManager authorizations, IOpenIddictTokenManager tokens) : ISessionRevocationHandler
{
    /// <summary>The authorization property holding the session id.</summary>
    public const string SessionProperty = SessionClaimTypeConstants.SessionId;

    /// <summary>
    /// Create the authorization the tokens of one authorization request hang off, naming <paramref name="sessionId"/>,
    /// and attach it to <paramref name="principal"/>, so OpenIddict stores the tokens under it.
    /// </summary>
    /// <param name="authorizations">OpenIddict's authorization manager.</param>
    /// <param name="applicationId">The client's application id.</param>
    /// <param name="subject">The person's id, as <c>sub</c>.</param>
    /// <param name="sessionId">The sign-in session.</param>
    /// <param name="principal">The principal the tokens are issued for, carrying the granted scopes.</param>
    /// <param name="now">The creation instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task AttachAsync(
        IOpenIddictAuthorizationManager authorizations,
        string applicationId,
        string subject,
        Guid sessionId,
        ClaimsPrincipal principal,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var descriptor = new OpenIddictAuthorizationDescriptor
        {
            ApplicationId = applicationId,
            CreationDate = now,
            Principal = principal,
            Status = Statuses.Valid,
            Subject = subject,
            Type = AuthorizationTypes.AdHoc,
        };
        descriptor.Scopes.UnionWith(principal.GetScopes());
        descriptor.Properties[SessionProperty] = JsonSerializer.SerializeToElement(sessionId.ToString("D", CultureInfo.InvariantCulture));

        var authorization = await authorizations.CreateAsync(descriptor, cancellationToken);
        principal.SetAuthorizationId(await authorizations.GetIdAsync(authorization, cancellationToken));
    }

    /// <inheritdoc />
    public async Task HandleAsync(IReadOnlyList<IdentitySession> sessions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        foreach (var person in sessions.GroupBy(session => session.UserId))
        {
            var ended = person.Select(session => session.Id.ToString("D", CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);

            // Read the person's authorizations first: the store streams them over the connection the revocations need.
            var candidates = new List<object>();
            await foreach (var authorization in authorizations.FindBySubjectAsync(person.Key.ToString("D", CultureInfo.InvariantCulture), cancellationToken))
                candidates.Add(authorization);

            foreach (var authorization in candidates)
            {
                var properties = await authorizations.GetPropertiesAsync(authorization, cancellationToken);
                if (!properties.TryGetValue(SessionProperty, out var sid) || sid.ValueKind != JsonValueKind.String || !ended.Contains(sid.GetString()!))
                    continue;

                await authorizations.TryRevokeAsync(authorization, cancellationToken);
                if (await authorizations.GetIdAsync(authorization, cancellationToken) is { } id)
                    await tokens.RevokeByAuthorizationIdAsync(id, cancellationToken);
            }
        }
    }
}

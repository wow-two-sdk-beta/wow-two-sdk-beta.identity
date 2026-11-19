using System.Globalization;
using Identity.Api.Settings;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;

namespace Identity.Api.AccountDeletion;

/// <summary>
/// Decides one call of the service deletion route and writes its audit row. A deletion client reaches only a person who
/// authorized one of its product's clients, so a leaked deletion secret cannot erase another product's people:
/// <list type="number">
///   <item>no authorization for any of the caller's product clients: <see cref="ServiceDeletionOutcome.NotThisProductsPerson"/>;</item>
///   <item>an organization role or an instance role (a console permission): <see cref="ServiceDeletionOutcome.HoldsAccess"/>;</item>
///   <item>authorizations for clients outside the product: those of the product are revoked with their tokens and the person stays
///     (<see cref="ServiceDeletionOutcome.Kept"/>);</item>
///   <item>otherwise the person is erased (<see cref="ServiceDeletionOutcome.Erased"/>), which an erased person's retry meets again.</item>
/// </list>
/// Every call writes a row to the sign-in log (<see cref="ServiceDeletionAuditConstants"/>).
/// </summary>
/// <remarks>
/// Authorizations of every status count, so a retry after a lost response finds the same footprint an erase leaves behind, and
/// a person who once signed in to another product is never erased for being a product's person.
/// </remarks>
/// <param name="settings">The deletion clients and the products they own.</param>
/// <param name="context">The identity context, for organization and instance roles.</param>
/// <param name="accounts">Finds the person.</param>
/// <param name="personalData">Erases the person.</param>
/// <param name="applications">OpenIddict's application manager, to name a client by an authorization.</param>
/// <param name="authorizations">OpenIddict's authorization manager.</param>
/// <param name="tokens">OpenIddict's token manager.</param>
/// <param name="signInLog">The log the audit rows go to.</param>
public sealed class ServiceAccountDeletionService(
    AccountDeletionSettings settings,
    IdentityDbContext context,
    UserAccountService accounts,
    [FromKeyedServices(ServiceAccountDeletionService.PersonalDataKey)] UserPersonalDataService personalData,
    IOpenIddictApplicationManager applications,
    IOpenIddictAuthorizationManager authorizations,
    IOpenIddictTokenManager tokens,
    SignInLogService signInLog)
{
    /// <summary>The service key of the personal-data service the route erases with.</summary>
    public const string PersonalDataKey = "identity-service-personal-data";

    /// <summary>Delete, keep or refuse <paramref name="personId"/> for <paramref name="callerClientId"/>, and audit the call.</summary>
    /// <param name="callerClientId">The deletion client the token names.</param>
    /// <param name="personId">The person.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ServiceDeletionOutcome> DeleteAsync(string callerClientId, Guid personId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerClientId);
        var (outcome, result, reason) = await DecideAsync(callerClientId, personId, cancellationToken);
        signInLog.ClientId = callerClientId;
        await signInLog.RecordAsync(
            new SignInAttempt { Method = ServiceDeletionAuditConstants.Method, Result = result, Reason = reason, UserId = personId },
            cancellationToken);
        return outcome;
    }

    private async Task<(ServiceDeletionOutcome Outcome, string Result, string? Reason)> DecideAsync(string callerClientId, Guid personId, CancellationToken cancellationToken)
    {
        var products = settings.Clients.TryGetValue(callerClientId, out var client)
            ? client.ProductClientIds.ToHashSet(StringComparer.Ordinal)
            : [];

        var grants = await ReadAuthorizationsAsync(personId, cancellationToken);
        var own = grants.Where(grant => products.Contains(grant.ClientId)).ToList();
        if (own.Count == 0)
            return (ServiceDeletionOutcome.NotThisProductsPerson, ServiceDeletionAuditConstants.NotFound, ServiceDeletionAuditConstants.NotProductPerson);

        if (await accounts.FindByIdAsync(personId, cancellationToken) is not { } person)
            return (ServiceDeletionOutcome.Erased, ServiceDeletionAuditConstants.Erased, null);

        if (await context.Set<IdentityMembership>().AsNoTracking().AnyAsync(row => row.UserId == personId, cancellationToken))
            return (ServiceDeletionOutcome.HoldsAccess, ServiceDeletionAuditConstants.Blocked, ServiceDeletionAuditConstants.OrganizationRole);

        if (await context.Set<IdentityUserRole>().AsNoTracking().AnyAsync(row => row.UserId == personId, cancellationToken))
            return (ServiceDeletionOutcome.HoldsAccess, ServiceDeletionAuditConstants.Blocked, ServiceDeletionAuditConstants.ConsolePermission);

        if (grants.Count > own.Count)
        {
            foreach (var grant in own)
            {
                await authorizations.TryRevokeAsync(grant.Authorization, cancellationToken);
                if (await authorizations.GetIdAsync(grant.Authorization, cancellationToken) is { } id)
                    await tokens.RevokeByAuthorizationIdAsync(id, cancellationToken);
            }

            return (ServiceDeletionOutcome.Kept, ServiceDeletionAuditConstants.Kept, ServiceDeletionAuditConstants.OtherProducts);
        }

        await personalData.DeleteAsync(person, cancellationToken);
        return (ServiceDeletionOutcome.Erased, ServiceDeletionAuditConstants.Erased, null);
    }

    /// <summary>Every authorization of the person, whatever its status, with the client it was granted to.</summary>
    private async Task<List<Grant>> ReadAuthorizationsAsync(Guid personId, CancellationToken cancellationToken)
    {
        // Read them all first: the store streams them over the connection the lookups and revocations need.
        var found = new List<object>();
        await foreach (var authorization in authorizations.FindBySubjectAsync(personId.ToString("D", CultureInfo.InvariantCulture), cancellationToken))
            found.Add(authorization);

        var clients = new Dictionary<string, string?>(StringComparer.Ordinal);
        var grants = new List<Grant>(found.Count);
        foreach (var authorization in found)
        {
            if (await authorizations.GetApplicationIdAsync(authorization, cancellationToken) is not { Length: > 0 } applicationId)
                continue;

            if (!clients.TryGetValue(applicationId, out var clientId))
            {
                clientId = await applications.FindByIdAsync(applicationId, cancellationToken) is { } application
                    ? await applications.GetClientIdAsync(application, cancellationToken)
                    : null;
                clients[applicationId] = clientId;
            }

            // An authorization of a client that left configuration names no product; it counts as another one's, so the person stays.
            grants.Add(new Grant(authorization, clientId ?? string.Empty));
        }

        return grants;
    }

    private readonly record struct Grant(object Authorization, string ClientId);
}

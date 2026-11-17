using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Identity.Members;

/// <summary>
/// Integrates Identity's organization members for services: who belongs to an organization, in which role, and whether the
/// instance's Telegram bot can reach them. Form 1 reads the module in process; Forms 2 and 3 call
/// <c>GET /api/identity/v1/organizations/{organizationId}/members</c> with a client-credentials token carrying
/// <see cref="MemberConstants.Scope"/>.
/// </summary>
/// <remarks>
///   - a member reads only their <c>sub</c>, role and Telegram flag: no name, phone, email or chat
///   - a failure carries its code in the error's <c>identityCode</c> metadata: <c>OrganizationNotFound</c> for an organization
///     the instance lacks (or a client bound to another one), <c>UnknownOrganizationRole</c> for a role outside the catalog,
///     <see cref="MemberConstants.UnavailableCode"/> when the instance cannot be reached
/// </remarks>
public interface IMemberBroker
{
    /// <summary>Lists the organization's members, earliest joined first, optionally only those holding <paramref name="role"/>.</summary>
    /// <param name="organizationId">The organization's id at the instance.</param>
    /// <param name="role">A catalog role (<c>owner</c>, <c>admin</c>, <c>editor</c>, <c>viewer</c>, <c>billing</c>, any case); null lists every member.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<Result<IReadOnlyList<MemberModel>>> ListAsync(Guid organizationId, string? role = null, CancellationToken cancellationToken = default);
}

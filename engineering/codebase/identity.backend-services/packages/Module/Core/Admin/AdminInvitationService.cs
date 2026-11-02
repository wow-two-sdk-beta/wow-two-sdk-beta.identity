using System.Globalization;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;

/// <summary>
/// Provides the invitations a host makes (<c>admin invite</c>), such as the first one an organization's second owner joins
/// by: any catalog role, an owner included, recorded as made by <see cref="Guid.Empty"/>. Each run makes a new invitation.
/// </summary>
/// <param name="invitations">Makes the invitation and its link.</param>
/// <param name="organizations">Finds the organization by handle or id.</param>
/// <param name="phones">The allowed countries; null when the phone slice is not registered, which allows every known country.</param>
public sealed class AdminInvitationService(InvitationService invitations, OrganizationService organizations, PhoneNumberPolicy? phones = null)
{
    /// <summary>Makes the invitation, or reports why it was not made; see <see cref="AdminInvitationService"/>.</summary>
    /// <param name="request">The organization, role and optional phone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<AdminInvitationResult> CreateAsync(AdminInvitationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = request.Organization.Trim();
        var organization = Guid.TryParse(key, CultureInfo.InvariantCulture, out var id)
            ? await organizations.FindByIdAsync(id, cancellationToken)
            : await organizations.FindBySlugAsync(key.ToLowerInvariant(), cancellationToken);
        if (organization is null)
            return AdminInvitationResult.Refused($"No organization has the handle or id '{key}'.");

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            var phone = PhoneNumberPolicy.Normalize(request.PhoneNumber);
            if (phone is null)
                return AdminInvitationResult.Invalid($"'{request.PhoneNumber}' is not a phone number in international form, such as +998901234567.", organization.Id);
            if (!(phones?.IsAllowed(phone) ?? PhoneNumberPolicy.CountriesOf(phone).Count > 0))
                return AdminInvitationResult.Invalid($"'{phone}' is outside the instance's allowed countries (Identity:Phone:AllowedCountries).", organization.Id);
        }

        var created = await invitations.CreateAsync(organization, Guid.Empty, request.Role, request.PhoneNumber, email: null, cancellationToken);
        if (!created.IsSuccess)
            return AdminInvitationResult.Invalid(created.Result.Errors[0].Description, organization.Id);

        return new AdminInvitationResult
        {
            Status = AdminInvitationStatus.Created,
            OrganizationId = organization.Id,
            InvitationId = created.Invitation!.Id,
            Role = created.Invitation.Role,
            Link = created.Link,
            Token = created.Token,
            ExpiresAt = created.Invitation.ExpiresAt,
        };
    }
}

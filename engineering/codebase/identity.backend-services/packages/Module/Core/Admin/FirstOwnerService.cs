using System.Net.Mail;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Naming;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;

/// <summary>
/// Provides the first organization of an instance and its owner, the one person a host creates (<c>admin create</c>);
/// members are managed elsewhere. Idempotent: a run whose organization and owner already exist, the person as an owner,
/// changes nothing and answers <see cref="FirstOwnerStatus.Present"/>; a run that stopped halfway finishes on the next.
/// </summary>
/// <remarks>
///   - the person is found by the given id, email or confirmed phone; two of them naming different people, or a person
///     holding other contacts or another id than given, is refused
///   - the organization is found by its handle; a handle or an id already held by another organization is refused, and so
///     is an existing organization whose owners do not include the person
///   - a new person holds the given phone and email confirmed and no password: they sign in by phone, or set a password
///     through the reset link, which proves the email first
///   - a new person's user name is the email, else the phone, else a generated one when that is taken
/// </remarks>
/// <param name="accounts">Creates and finds people.</param>
/// <param name="users">Finds the person holding a confirmed phone.</param>
/// <param name="organizations">Creates and finds organizations and reads roles.</param>
/// <param name="options">The owner role.</param>
/// <param name="phones">The allowed countries; null when the phone slice is not registered, which allows every known country.</param>
public sealed class FirstOwnerService(
    UserAccountService accounts,
    IUserRepository users,
    OrganizationService organizations,
    IOptionsMonitor<OrganizationOptions> options,
    PhoneNumberPolicy? phones = null)
{
    /// <summary>Creates the organization and its owner, or reports that both exist; see <see cref="FirstOwnerService"/>.</summary>
    /// <param name="request">The organization and the owner.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<FirstOwnerResult> CreateAsync(FirstOwnerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Check(request, out var phone, out var email) is { } invalid)
            return FirstOwnerResult.Invalid(invalid);

        var slug = request.Slug.Trim().ToLowerInvariant();
        var organization = await organizations.FindBySlugAsync(slug, cancellationToken);
        if (request.OrganizationId is { } organizationId)
        {
            if (organization is not null && organization.Id != organizationId)
                return FirstOwnerResult.Refused($"The handle '{slug}' belongs to organization {organization.Id}, not {organizationId}.", organization.Id);
            if (organization is null && await organizations.FindByIdAsync(organizationId, cancellationToken) is { } other)
                return FirstOwnerResult.Refused($"Organization {organizationId} exists with the handle '{other.Slug}', not '{slug}'.", organizationId);
        }

        var found = await FindPersonAsync(request.UserId, email, phone, cancellationToken);
        if (found.Error is { } conflict)
            return FirstOwnerResult.Refused(conflict, organization?.Id, found.Person?.Id);

        var person = found.Person;
        if (organization is not null)
        {
            if (person is null)
                return FirstOwnerResult.Refused($"Organization '{slug}' exists and this person is not among its owners; members are managed in the console.", organization.Id);

            var role = await organizations.GetRoleAsync(organization, person, cancellationToken);
            return string.Equals(role, options.CurrentValue.OwnerRole, StringComparison.OrdinalIgnoreCase)
                ? new FirstOwnerResult { Status = FirstOwnerStatus.Present, OrganizationId = organization.Id, UserId = person.Id }
                : FirstOwnerResult.Refused($"Person {person.Id} is not an owner of '{slug}'; members are managed in the console.", organization.Id, person.Id);
        }

        var isOwnerCreated = false;
        if (person is null)
        {
            var created = await CreatePersonAsync(request, phone, email, cancellationToken);
            if (created.Error is { } refused)
                return FirstOwnerResult.Refused(refused);

            person = created.Person!;
            isOwnerCreated = true;
        }

        var newOrganization = new IdentityOrganization { Id = request.OrganizationId ?? Guid.Empty, Name = request.OrganizationName, Slug = slug };
        var made = await organizations.CreateAsync(newOrganization, person, cancellationToken);
        if (!made.IsSuccess)
            return FirstOwnerResult.Refused(made.Errors[0].Description, userId: person.Id) with { IsOwnerCreated = isOwnerCreated };

        return new FirstOwnerResult
        {
            Status = FirstOwnerStatus.Created,
            OrganizationId = newOrganization.Id,
            UserId = person.Id,
            IsOrganizationCreated = true,
            IsOwnerCreated = isOwnerCreated,
        };
    }

    /// <summary>The first problem of the request on its own, or null; the phone in E.164 and the trimmed email.</summary>
    private string? Check(FirstOwnerRequest request, out string? phone, out string? email)
    {
        phone = null;
        email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        if (OrganizationService.CheckShape(request.OrganizationName, request.Slug) is { IsSuccess: false } shape)
            return shape.Errors[0].Description;
        if (request.OrganizationId == Guid.Empty || request.UserId == Guid.Empty)
            return "A preset id must not be the empty uuid.";
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > IdentityDbContext.DisplayNameMaxLength)
            return $"The owner needs a display name of 1–{IdentityDbContext.DisplayNameMaxLength} characters.";
        if (email is null && string.IsNullOrWhiteSpace(request.PhoneNumber))
            return "The owner needs a phone, an email or both.";
        if (email is not null && (!MailAddress.TryCreate(email, out var address) || address.Address != email))
            return $"'{email}' is not an email address.";
        if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            phone = PhoneNumberPolicy.Normalize(request.PhoneNumber);
            if (phone is null)
                return $"'{request.PhoneNumber}' is not a phone number in international form, such as +998901234567.";
            if (!(phones?.IsAllowed(phone) ?? PhoneNumberPolicy.CountriesOf(phone).Count > 0))
                return $"'{phone}' is outside the instance's allowed countries (Identity:Phone:AllowedCountries).";
        }

        return null;
    }

    /// <summary>The one person the id, email and phone name, or why they conflict.</summary>
    private async Task<(IdentityUser? Person, string? Error)> FindPersonAsync(Guid? userId, string? email, string? phone, CancellationToken cancellationToken)
    {
        var byId = userId is { } id ? await accounts.FindByIdAsync(id, cancellationToken) : null;
        var byEmail = email is null ? null : await accounts.FindByEmailAsync(email, cancellationToken);
        var byPhone = phone is null ? null : await users.FindByConfirmedPhoneNumberAsync(phone, cancellationToken);
        var people = new[] { byId, byEmail, byPhone }.OfType<IdentityUser>().DistinctBy(person => person.Id).ToList();
        if (people.Count > 1)
            return (null, $"The id, email and phone name different people: {string.Join(", ", people.Select(person => person.Id))}.");
        if (people is not [var person])
            return (null, null);

        if (userId is { } wanted && person.Id != wanted)
            return (person, $"The email or phone belongs to person {person.Id}, not {wanted}.");
        if (email is not null && !string.Equals(person.NormalizedEmail, email.ToCanonical(), StringComparison.Ordinal))
            return (person, $"Person {person.Id} exists with another email.");
        if (phone is not null && !(person.IsPhoneNumberConfirmed && string.Equals(person.PhoneNumber, phone, StringComparison.Ordinal)))
            return (person, $"Person {person.Id} exists with another phone.");

        return (person, null);
    }

    /// <summary>Creates the owner holding the given contacts confirmed, under the email, the phone or a generated user name.</summary>
    private async Task<(IdentityUser? Person, string? Error)> CreatePersonAsync(FirstOwnerRequest request, string? phone, string? email, CancellationToken cancellationToken)
    {
        var id = request.UserId ?? Guid.Empty;
        foreach (var userName in new[] { email ?? phone!, null })
        {
            var person = new IdentityUser
            {
                Id = id,
                UserName = userName ?? "owner-" + Guid.NewGuid().ToString("N"),
                DisplayName = request.DisplayName.Trim(),
                Email = email,
                IsEmailConfirmed = email is not null,
                PhoneNumber = phone,
                IsPhoneNumberConfirmed = phone is not null,
            };
            var created = await accounts.CreateAsync(person, cancellationToken);
            if (created.IsSuccess)
                return (person, null);
            if (created.Errors.All(error => error.Code != IdentityErrorCodeConstants.DuplicateUserName))
                return (null, created.Errors[0].Description);
        }

        return (null, "No user name was free for the owner.");
    }
}

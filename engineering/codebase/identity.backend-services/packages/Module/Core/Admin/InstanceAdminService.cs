using System.Net.Mail;
using WoW.Two.Sdk.Beta.Be.Foundation.Naming;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;

/// <summary>
/// Provides the instance's first admin, the one person a host creates (<c>admin create</c>): the person holding the
/// instance role <c>admin</c>, who then creates organizations in the console. Only once: while anyone else holds
/// <c>admin</c>, a run is refused. Idempotent: a run naming the person who holds it changes nothing and answers
/// <see cref="InstanceAdminStatus.Present"/>; a run that stopped halfway finishes on the next.
/// </summary>
/// <remarks>
///   - the person is found by the given id, email or confirmed phone; two of them naming different people, or a person
///     holding other contacts or another id than given, is refused
///   - a person found by email alone whose email is unconfirmed is refused: anyone can sign up with an address they do
///     not hold, so only a confirmed email, a confirmed phone or the person's id (<c>--user-id</c>) names the admin
///   - the named person holding <c>admin</c> answers present, whoever else holds it; anyone else holding it refuses the run
///     before any write
///   - a new person holds the given phone and email confirmed and no password: they sign in by phone, or set a password
///     through the reset link, which proves the email first
///   - a new person's user name is the email, else the phone, else a generated one when that is taken
///   - an existing person without <c>admin</c> is given it, seeding the role's row; nobody else's roles change
///   - built from the host's services (<c>ActivatorUtilities</c>), so it needs the roles slice (<c>AddRoles</c>) and no
///     registration of its own
/// </remarks>
/// <param name="accounts">Creates and finds people.</param>
/// <param name="users">Finds the person holding a confirmed phone.</param>
/// <param name="instanceRoles">Gives the instance role and lists who holds it.</param>
/// <param name="phones">The allowed countries; null when the phone slice is not registered, which allows every known country.</param>
public sealed class InstanceAdminService(
    UserAccountService accounts,
    IUserRepository users,
    InstanceRoleService instanceRoles,
    PhoneNumberPolicy? phones = null)
{
    /// <summary>Creates the admin, or reports that they hold <c>admin</c> already; see <see cref="InstanceAdminService"/>.</summary>
    /// <param name="request">The admin.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<InstanceAdminResult> CreateAsync(InstanceAdminRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Check(request, out var phone, out var email) is { } invalid)
            return InstanceAdminResult.Invalid(invalid);

        var found = await FindPersonAsync(request.UserId, email, phone, cancellationToken);
        if (found.Error is { } conflict)
            return InstanceAdminResult.Refused(conflict, found.Person?.Id);

        var person = found.Person;
        var holders = await instanceRoles.GetHoldersAsync(InstanceRoleConstants.Admin, cancellationToken);
        if (person is not null && holders.Contains(person.Id))
            return new InstanceAdminResult { Status = InstanceAdminStatus.Present, UserId = person.Id };
        if (holders.Count > 0)
        {
            return InstanceAdminResult.Refused(
                $"The instance admin is already {string.Join(", ", holders)}; admin create makes the first admin only, once.",
                person?.Id);
        }

        var isUserCreated = false;
        if (person is null)
        {
            var created = await CreatePersonAsync(request, phone, email, cancellationToken);
            if (created.Error is { } refused)
                return InstanceAdminResult.Refused(refused);

            person = created.Person!;
            isUserCreated = true;
        }

        var grant = await instanceRoles.GrantAsync(person, InstanceRoleConstants.Admin, cancellationToken);
        return grant.IsSuccess
            ? new InstanceAdminResult { Status = InstanceAdminStatus.Created, UserId = person.Id, IsUserCreated = isUserCreated, IsRoleGranted = grant.IsGranted }
            : InstanceAdminResult.Refused(grant.Result.Errors[0].Description, person.Id) with { IsUserCreated = isUserCreated };
    }

    /// <summary>The first problem of the request on its own, or null; the phone in E.164 and the trimmed email.</summary>
    private string? Check(InstanceAdminRequest request, out string? phone, out string? email)
    {
        phone = null;
        email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        if (request.UserId == Guid.Empty)
            return "A preset id must not be the empty uuid.";
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > IdentityDbContext.DisplayNameMaxLength)
            return $"The admin needs a display name of 1–{IdentityDbContext.DisplayNameMaxLength} characters.";
        if (email is null && string.IsNullOrWhiteSpace(request.PhoneNumber))
            return "The admin needs a phone, an email or both.";
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

        // A sign-up with someone else's address is not proof: only the id or a confirmed contact names the admin.
        if (byId is null && byPhone is null && !person.IsEmailConfirmed)
            return (person, $"Person {person.Id} holds '{email}' unconfirmed, so it may not be theirs; confirm the email first, or name them with --user-id.");

        return (person, null);
    }

    /// <summary>Creates the admin holding the given contacts confirmed, under the email, the phone or a generated user name.</summary>
    private async Task<(IdentityUser? Person, string? Error)> CreatePersonAsync(InstanceAdminRequest request, string? phone, string? email, CancellationToken cancellationToken)
    {
        var id = request.UserId ?? Guid.Empty;
        foreach (var userName in new[] { email ?? phone!, null })
        {
            var person = new IdentityUser
            {
                Id = id,
                UserName = userName ?? "admin-" + Guid.NewGuid().ToString("N"),
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

        return (null, "No user name was free for the admin.");
    }
}

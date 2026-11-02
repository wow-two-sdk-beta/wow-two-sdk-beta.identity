using System.Buffers.Text;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;

/// <summary>
/// Provides invitations into an organization: an owner or admin makes a single-use link carrying a role, which signs a new
/// person up (even while sign-up is by invitation only) or adds a signed-in person, with that role. An invitation naming a
/// phone number or an email redeems only for that identifier.
/// </summary>
/// <remarks>
///   - the token is 32 random bytes, base64url, shown once; only its SHA-256 is stored
///   - redemption is a conditional update, so one invitation joins one person; a person already in the organization keeps
///     their role and spends the invitation
///   - a named phone number must be the person's confirmed number; a named email must be the person's email, which the
///     instance's sign-in rules may still ask them to confirm
/// </remarks>
/// <param name="invitations">The invitation store.</param>
/// <param name="organizations">The organizations, for roles and memberships.</param>
/// <param name="context">The identity context, for redeeming within a sign-up's transaction.</param>
/// <param name="options">The lifetime and link; <c>Identity:Invitations</c> reloads live.</param>
/// <param name="time">The clock invitations are stamped with.</param>
public sealed class InvitationService(
    IInvitationRepository invitations,
    OrganizationService organizations,
    IdentityDbContext context,
    IOptionsMonitor<InvitationOptions> options,
    TimeProvider time)
{
    private const int SecretLength = 32;

    /// <summary>Make an invitation into <paramref name="organization"/> with <paramref name="role"/>, optionally for one phone number or email.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="creator">The owner or admin making it; the caller checks that they may.</param>
    /// <param name="role">The role the person joins with, one of the organization roles.</param>
    /// <param name="phoneNumber">The phone number that alone may redeem it, as typed; null for none.</param>
    /// <param name="email">The email that alone may redeem it; null for none.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<InvitationCreateResult> CreateAsync(
        IdentityOrganization organization,
        IdentityUser creator,
        string? role,
        string? phoneNumber,
        string? email,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(creator);
        return CreateAsync(organization, creator.Id, role, phoneNumber, email, cancellationToken);
    }

    /// <summary>
    /// Make an invitation into <paramref name="organization"/> with <paramref name="role"/> on behalf of
    /// <paramref name="creatorId"/>, such as <see cref="Guid.Empty"/> for the host CLI (<c>admin invite</c>).
    /// </summary>
    /// <param name="organization">The organization.</param>
    /// <param name="creatorId">Who makes it; the caller checks that they may.</param>
    /// <param name="role">The role the person joins with, one of the organization roles.</param>
    /// <param name="phoneNumber">The phone number that alone may redeem it, as typed; null for none.</param>
    /// <param name="email">The email that alone may redeem it; null for none.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<InvitationCreateResult> CreateAsync(
        IdentityOrganization organization,
        Guid creatorId,
        string? role,
        string? phoneNumber,
        string? email,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organization);
        if (organizations.FindRole(role) is not { } resolved)
            return Refused(IdentityUserExtensions.Failure(IdentityErrorCodeConstants.UnknownOrganizationRole, $"Role '{role}' is not configured for organizations.", IdentityUserExtensions.Param("Role", role)));

        string? phone = null;
        if (!string.IsNullOrWhiteSpace(phoneNumber) && (phone = PhoneNumberPolicy.Normalize(phoneNumber)) is null)
            return Refused(InvalidIdentifier("The phone number is not a valid international number."));

        string? address = null;
        if (!string.IsNullOrWhiteSpace(email) && (address = NormalizeEmail(email)) is null)
            return Refused(InvalidIdentifier("The email is not a valid address."));

        var now = time.GetUtcNow();
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretLength));
        var invitation = new IdentityInvitation
        {
            Id = Guid.CreateVersion7(now),
            OrganizationId = organization.Id,
            Role = resolved,
            TokenHash = HashOf(token),
            PhoneNumber = phone,
            Email = address,
            CreatedByUserId = creatorId,
            CreatedAt = now,
            ExpiresAt = now + options.CurrentValue.Lifetime,
        };
        await invitations.AddAsync(invitation, cancellationToken);
        return new InvitationCreateResult
        {
            Result = IdentityResult.Success,
            Invitation = invitation,
            Token = token,
            Link = options.CurrentValue.LinkTemplate?.Replace("{token}", token, StringComparison.Ordinal),
        };
    }

    /// <summary>The organization's invitations, newest first.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<IReadOnlyList<IdentityInvitation>> ListAsync(IdentityOrganization organization, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organization);
        return invitations.ListAsync(organization.Id, cancellationToken);
    }

    /// <summary>Withdraw the organization's invitation; false when it is not the organization's, or already used or withdrawn.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="invitationId">The invitation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<bool> RevokeAsync(IdentityOrganization organization, Guid invitationId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organization);
        return invitations.TryRevokeAsync(organization.Id, invitationId, time.GetUtcNow(), cancellationToken);
    }

    /// <summary>The invitation the link's <paramref name="token"/> names, with its organization and where it stands; null when none does.</summary>
    /// <param name="token">The token from the link.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<InvitationLookup?> FindAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
            return null;

        var invitation = await invitations.FindByTokenHashAsync(HashOf(token.Trim()), cancellationToken);
        if (invitation is null || await organizations.FindByIdAsync(invitation.OrganizationId, cancellationToken) is not { } organization)
            return null;

        return new InvitationLookup { Invitation = invitation, Organization = organization, Status = StatusOf(invitation) };
    }

    /// <summary>The invitation with <paramref name="id"/>, with its organization and where it stands; null when none has it.</summary>
    /// <param name="id">The invitation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<InvitationLookup?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invitation = await invitations.FindByIdAsync(id, cancellationToken);
        if (invitation is null || await organizations.FindByIdAsync(invitation.OrganizationId, cancellationToken) is not { } organization)
            return null;

        return new InvitationLookup { Invitation = invitation, Organization = organization, Status = StatusOf(invitation) };
    }

    /// <summary>Where <paramref name="invitation"/> stands now.</summary>
    /// <param name="invitation">The invitation.</param>
    public InvitationStatus StatusOf(IdentityInvitation invitation)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        if (invitation.RevokedAt is not null)
            return InvitationStatus.Revoked;
        if (invitation.AcceptedAt is not null)
            return InvitationStatus.Accepted;
        return invitation.ExpiresAt <= time.GetUtcNow() ? InvitationStatus.Expired : InvitationStatus.Pending;
    }

    /// <summary>
    /// Whether the person about to sign up with <paramref name="email"/> or <paramref name="phoneNumber"/> may redeem
    /// <paramref name="invitation"/>: it is pending, and an identifier it names is the one given.
    /// </summary>
    /// <param name="invitation">The invitation.</param>
    /// <param name="email">The email signing up; null for a phone sign-up.</param>
    /// <param name="phoneNumber">The phone number signing up, in E.164; null for an email sign-up.</param>
    public IdentityResult Check(IdentityInvitation invitation, string? email, string? phoneNumber)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        var status = StatusOf(invitation);
        if (status != InvitationStatus.Pending)
            return RefusalOf(status);

        return Names(invitation, email, phoneNumber) ? IdentityResult.Success : Mismatch();
    }

    /// <summary>
    /// Redeem <paramref name="invitation"/> for <paramref name="user"/>: spend it and add them to its organization with its
    /// role; a member already keeps their role. Refused when it is not pending or names another identifier.
    /// </summary>
    /// <param name="invitation">The invitation.</param>
    /// <param name="user">The person redeeming it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentityResult> RedeemAsync(IdentityInvitation invitation, IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        ArgumentNullException.ThrowIfNull(user);
        var status = StatusOf(invitation);
        if (status != InvitationStatus.Pending)
            return RefusalOf(status);
        var phone = user.IsPhoneNumberConfirmed ? user.PhoneNumber : null;
        if (!Names(invitation, user.Email, phone))
            return Mismatch();
        if (await organizations.FindByIdAsync(invitation.OrganizationId, cancellationToken) is not { } organization)
            return IdentityUserExtensions.Failure(IdentityErrorCodeConstants.InvitationNotFound, "The invitation's organization no longer exists.");
        if (organizations.FindRole(invitation.Role) is not { } role)
            return IdentityUserExtensions.Failure(IdentityErrorCodeConstants.UnknownOrganizationRole, $"Role '{invitation.Role}' is no longer configured for organizations.", IdentityUserExtensions.Param("Role", invitation.Role));
        if (!await invitations.TryAcceptAsync(invitation.Id, user.Id, time.GetUtcNow(), cancellationToken))
            return RefusalOf(InvitationStatus.Accepted);

        return await organizations.GetRoleAsync(organization, user, cancellationToken) is not null
            ? IdentityResult.Success
            : await organizations.AddMemberAsync(organization, user, role, cancellationToken);
    }

    /// <summary>
    /// Run a sign-up <paramref name="step"/> and redeem <paramref name="invitation"/> for the person it signs up, in one
    /// transaction: a refused redemption undoes the step's writes too. A step that signs nobody up keeps its writes (a
    /// failed code attempt stays counted) and redeems nothing.
    /// </summary>
    /// <typeparam name="T">The step's outcome.</typeparam>
    /// <param name="invitation">The invitation.</param>
    /// <param name="step">The sign-up, such as creating the person or verifying their phone code.</param>
    /// <param name="personOf">The person <paramref name="step"/> signed up, or null when it failed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The step's outcome, and the redemption; a null redemption means the step signed nobody up.</returns>
    public async Task<(T Step, IdentityResult? Redemption)> RedeemWithAsync<T>(
        IdentityInvitation invitation,
        Func<CancellationToken, Task<T>> step,
        Func<T, IdentityUser?> personOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(personOf);
        await using var transaction = context.Database.CurrentTransaction is null && context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var outcome = await step(cancellationToken);
        if (personOf(outcome) is not { } person)
        {
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return (outcome, null);
        }

        var redemption = await RedeemAsync(invitation, person, cancellationToken);
        if (transaction is not null)
        {
            if (redemption.IsSuccess)
                await transaction.CommitAsync(cancellationToken);
            else
                await transaction.RollbackAsync(cancellationToken);
        }

        if (!redemption.IsSuccess)
            context.ChangeTracker.Clear();
        return (outcome, redemption);
    }

    /// <summary>The refusal of an invitation that does not stand at <paramref name="status"/>.</summary>
    /// <param name="status">Where it stands.</param>
    public static IdentityResult RefusalOf(InvitationStatus status) => status switch
    {
        InvitationStatus.Expired => IdentityUserExtensions.Failure(IdentityErrorCodeConstants.InvitationExpired, "The invitation has expired; ask for a new one."),
        InvitationStatus.Accepted => IdentityUserExtensions.Failure(IdentityErrorCodeConstants.InvitationAccepted, "The invitation was already used; ask for a new one."),
        InvitationStatus.Revoked => IdentityUserExtensions.Failure(IdentityErrorCodeConstants.InvitationRevoked, "The invitation was withdrawn."),
        _ => IdentityResult.Success,
    };

    /// <summary>A refusal for a link whose token names no invitation.</summary>
    public static IdentityResult NotFound()
        => IdentityUserExtensions.Failure(IdentityErrorCodeConstants.InvitationNotFound, "The invitation link is not valid.");

    private static bool Names(IdentityInvitation invitation, string? email, string? phoneNumber)
        => (invitation.PhoneNumber is null || string.Equals(invitation.PhoneNumber, phoneNumber, StringComparison.Ordinal))
           && (invitation.Email is null || string.Equals(invitation.Email, email?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static IdentityResult Mismatch()
        => IdentityUserExtensions.Failure(IdentityErrorCodeConstants.InvitationIdentifierMismatch, "The invitation is for another phone number or email.");

    private static IdentityResult InvalidIdentifier(string message)
        => IdentityUserExtensions.Failure(IdentityErrorCodeConstants.InvalidInvitationIdentifier, message);

    private static InvitationCreateResult Refused(IdentityResult result) => new() { Result = result };

    private static string? NormalizeEmail(string email)
    {
        var trimmed = email.Trim();
        return trimmed.Length <= 256 && MailAddress.TryCreate(trimmed, out var parsed) && string.Equals(parsed.Address, trimmed, StringComparison.Ordinal)
            ? trimmed.ToLowerInvariant()
            : null;
    }

    private static string HashOf(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

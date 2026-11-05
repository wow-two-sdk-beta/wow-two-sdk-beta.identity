using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;
using WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

/// <summary>Maps the organization management API: members and, with invitations registered, invitation links.</summary>
public static class OrganizationEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps, under <paramref name="endpoints"/> (<c>/api/organizations</c> in the service), for the organization's owners and
    /// admins: <c>GET {organizationId}/members</c>; <c>PUT {organizationId}/members/{userId}</c> with <c>{ role }</c>;
    /// <c>DELETE {organizationId}/members/{userId}</c>; with invitations registered, <c>POST {organizationId}/invitations</c>
    /// with <c>{ role, phoneNumber?, email? }</c> (the answer carries the link once), <c>GET {organizationId}/invitations</c> and
    /// <c>DELETE {organizationId}/invitations/{invitationId}</c>; and, with the Telegram bot registered,
    /// <c>POST {organizationId}/members/{userId}/telegram-change/approve</c> and <c>…/deny</c>.
    /// </summary>
    /// <remarks>
    ///   - a signed-in person outside the organization gets <c>404</c> (<c>OrganizationNotFound</c>), a member who is neither
    ///     owner nor admin <c>403</c> (<c>OrganizationManagerRequired</c>)
    ///   - owners and admins manage every member alike: either invites with any catalog role, promotes to or demotes from
    ///     owner, removes anyone and resolves anyone's Telegram change; an organization may be left with no owner
    ///   - a role change or removal signs nobody out; the member's next token carries it, and a signal tells the services
    ///   - approving a member's pending Telegram change moves their confirmed number's link to the Telegram account that
    ///     asked and rotates nothing else; denying drops it; none pending answers <c>404</c> (<c>TelegramChangeNotFound</c>)
    ///   - failures carry their code in the problem's <c>identityCode</c> extension
    /// </remarks>
    /// <param name="endpoints">The route builder or group.</param>
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet("/{organizationId:guid}/members", ListMembersAsync).RequireAuthorization();
        endpoints.MapPut("/{organizationId:guid}/members/{userId:guid}", ChangeRoleAsync).RequireAuthorization();
        endpoints.MapDelete("/{organizationId:guid}/members/{userId:guid}", RemoveMemberAsync).RequireAuthorization();
        if (endpoints.ServiceProvider.GetService<IServiceProviderIsService>()?.IsService(typeof(InvitationService)) == true)
        {
            endpoints.MapPost("/{organizationId:guid}/invitations", CreateInvitationAsync).RequireAuthorization();
            endpoints.MapGet("/{organizationId:guid}/invitations", ListInvitationsAsync).RequireAuthorization();
            endpoints.MapDelete("/{organizationId:guid}/invitations/{invitationId:guid}", RevokeInvitationAsync).RequireAuthorization();
        }

        if (endpoints.ServiceProvider.GetService<IServiceProviderIsService>()?.IsService(typeof(TelegramLinkChangeService)) == true)
        {
            endpoints.MapPost("/{organizationId:guid}/members/{userId:guid}/telegram-change/approve", ApproveTelegramChangeAsync).RequireAuthorization();
            endpoints.MapPost("/{organizationId:guid}/members/{userId:guid}/telegram-change/deny", DenyTelegramChangeAsync).RequireAuthorization();
        }

        return endpoints;
    }

    private static async Task<IResult> ListMembersAsync(Guid organizationId, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var manager = await ManagerOfAsync(organizationId, principal, http, cancellationToken);
        var accounts = http.RequestServices.GetRequiredService<UserAccountService>();
        var found = new List<(IdentityMembership Membership, IdentityUser User)>();
        foreach (var membership in await manager.Organizations.ListMembersAsync(manager.Organization, cancellationToken))
        {
            if (await accounts.FindByIdAsync(membership.UserId, cancellationToken) is { } user)
                found.Add((membership, user));
        }

        var changes = http.RequestServices.GetService<TelegramLinkChangeService>();
        IReadOnlySet<string> pending = changes is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : await changes.FindPendingNumbersAsync([.. found.Select(member => ConfirmedPhoneOf(member.User)).OfType<string>().Distinct(StringComparer.Ordinal)], cancellationToken);
        var contacts = http.RequestServices.GetService<TrustedContactService>() is { } trusted
            ? await trusted.ContactsOfAsync([.. found.Select(member => member.User.Id)], cancellationToken)
            : new Dictionary<Guid, IReadOnlyList<IdentityUser>>();
        var members = found.Select(member => new OrganizationMemberDto
        {
            UserId = member.User.Id,
            DisplayName = member.User.DisplayName,
            UserName = member.User.UserName,
            Email = member.User.Email,
            PhoneNumber = member.User.PhoneNumber,
            Role = member.Membership.Role,
            JoinedAt = member.Membership.CreatedAt,
            HasPendingTelegramChange = ConfirmedPhoneOf(member.User) is { } phone && pending.Contains(phone),
            IsPhoneConfirmed = ConfirmedPhoneOf(member.User) is not null,
            TrustedContacts = contacts.TryGetValue(member.User.Id, out var trustedBy)
                ? [.. trustedBy.Select(contact => new MemberTrustedContactDto { UserId = contact.Id, DisplayName = TrustedContactService.NameOf(contact) })]
                : [],
        }).ToList();

        return Results.Ok(ApiResponse<IReadOnlyList<OrganizationMemberDto>>.Ok(members));
    }

    private static async Task<IResult> ChangeRoleAsync(Guid organizationId, Guid userId, MemberRoleApiRequest request, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var manager = await ManagerOfAsync(organizationId, principal, http, cancellationToken);
        var member = await MemberOfAsync(manager, userId, http, cancellationToken);
        var changed = await manager.Organizations.ChangeRoleAsync(manager.Organization, member, request.Role, cancellationToken);
        return changed.IsSuccess ? Results.NoContent() : throw InvitationErrors.Of(changed).ToException();
    }

    private static async Task<IResult> RemoveMemberAsync(Guid organizationId, Guid userId, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var manager = await ManagerOfAsync(organizationId, principal, http, cancellationToken);
        var member = await MemberOfAsync(manager, userId, http, cancellationToken);
        var removed = await manager.Organizations.RemoveMemberAsync(manager.Organization, member, cancellationToken);
        return removed.IsSuccess ? Results.NoContent() : throw InvitationErrors.Of(removed).ToException();
    }

    private static async Task<IResult> ApproveTelegramChangeAsync(Guid organizationId, Guid userId, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var phone = await TelegramChangePhoneOfAsync(organizationId, userId, principal, http, cancellationToken);
        return phone is not null && await http.RequestServices.GetRequiredService<TelegramLinkChangeService>().ApproveAsync(phone, cancellationToken)
            ? Results.NoContent()
            : throw TelegramChangeNotFound().ToException();
    }

    private static async Task<IResult> DenyTelegramChangeAsync(Guid organizationId, Guid userId, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var phone = await TelegramChangePhoneOfAsync(organizationId, userId, principal, http, cancellationToken);
        return phone is not null && await http.RequestServices.GetRequiredService<TelegramLinkChangeService>().DenyAsync(phone, cancellationToken)
            ? Results.NoContent()
            : throw TelegramChangeNotFound().ToException();
    }

    /// <summary>The member's confirmed number in E.164, after the member routes' checks; null when they hold none.</summary>
    private static async Task<string?> TelegramChangePhoneOfAsync(Guid organizationId, Guid userId, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var manager = await ManagerOfAsync(organizationId, principal, http, cancellationToken);
        return ConfirmedPhoneOf(await MemberOfAsync(manager, userId, http, cancellationToken));
    }

    /// <summary>The person's phone number in E.164 while it is confirmed; null otherwise.</summary>
    private static string? ConfirmedPhoneOf(IdentityUser user)
        => user.IsPhoneNumberConfirmed ? PhoneNumberPolicy.Normalize(user.PhoneNumber) : null;

    private static AppError TelegramChangeNotFound()
        => InvitationErrors.Coded(AppErrorType.NotFound, "No Telegram account change of the person's phone number is waiting for approval.", IdentityErrorCodeConstants.TelegramChangeNotFound);

    private static async Task<IResult> CreateInvitationAsync(Guid organizationId, InvitationApiRequest request, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var manager = await ManagerOfAsync(organizationId, principal, http, cancellationToken);
        var invitations = http.RequestServices.GetRequiredService<InvitationService>();
        if (request.TrustedContactUserId is not null)
        {
            var trusted = http.RequestServices.GetService<TrustedContactService>();
            var check = trusted is null
                ? IdentityUserExtensions.Failure(TrustedContactErrorCodeConstants.TrustedContactsOff, "Trusted contacts are off on this instance.")
                : await trusted.CheckInvitationContactAsync(manager.Organization, manager.Caller, request.TrustedContactUserId, cancellationToken);
            if (!check.IsSuccess)
                throw TrustedContactErrorOf(check).ToException();
        }

        var created = await invitations.CreateAsync(manager.Organization, manager.Caller, request.Role, request.PhoneNumber, request.Email, request.TrustedContactUserId, cancellationToken);
        if (!created.IsSuccess)
            throw InvitationErrors.Of(created.Result).ToException();

        return Results.Ok(ApiResponse<InvitationCreatedDto>.Ok(new InvitationCreatedDto
        {
            Invitation = ToDto(created.Invitation!, invitations),
            Token = created.Token!,
            Link = created.Link,
        }));
    }

    private static async Task<IResult> ListInvitationsAsync(Guid organizationId, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var manager = await ManagerOfAsync(organizationId, principal, http, cancellationToken);
        var invitations = http.RequestServices.GetRequiredService<InvitationService>();
        var listed = await invitations.ListAsync(manager.Organization, cancellationToken);
        return Results.Ok(ApiResponse<IReadOnlyList<InvitationDto>>.Ok([.. listed.Select(invitation => ToDto(invitation, invitations))]));
    }

    private static async Task<IResult> RevokeInvitationAsync(Guid organizationId, Guid invitationId, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var manager = await ManagerOfAsync(organizationId, principal, http, cancellationToken);
        return await http.RequestServices.GetRequiredService<InvitationService>().RevokeAsync(manager.Organization, invitationId, cancellationToken)
            ? Results.NoContent()
            : throw InvitationErrors.Of(InvitationService.NotFound()).ToException();
    }

    /// <summary>The organization and the signed-in caller, when they own or administer it; refused otherwise.</summary>
    private static async Task<Manager> ManagerOfAsync(Guid organizationId, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var organizations = services.GetRequiredService<OrganizationService>();
        var claimType = services.GetRequiredService<IdentityCoreOptions>().Claims.UserIdClaimType;
        var caller = Guid.TryParse(principal.FindFirst(claimType)?.Value, out var callerId)
            ? await services.GetRequiredService<UserAccountService>().FindByIdAsync(callerId, cancellationToken)
            : null;
        if (caller is null)
            throw AppErrorFactory.Unauthorized("The account no longer exists.").ToException();

        var organization = await organizations.FindByIdAsync(organizationId, cancellationToken);
        var role = organization is null ? null : await organizations.GetRoleAsync(organization, caller, cancellationToken);
        if (organization is null || role is null)
            throw InvitationErrors.Coded(AppErrorType.NotFound, "No such organization.", IdentityErrorCodeConstants.OrganizationNotFound).ToException();
        if (!organizations.IsManagerRole(role))
            throw InvitationErrors.Coded(AppErrorType.Forbidden, "Only an owner or admin manages the organization's members.", IdentityErrorCodeConstants.OrganizationManagerRequired).ToException();

        return new Manager(organizations, organization, caller);
    }

    /// <summary>The member <paramref name="userId"/>; <c>404</c> when they are not one.</summary>
    private static async Task<IdentityUser> MemberOfAsync(Manager manager, Guid userId, HttpContext http, CancellationToken cancellationToken)
    {
        var user = await http.RequestServices.GetRequiredService<UserAccountService>().FindByIdAsync(userId, cancellationToken);
        var role = user is null ? null : await manager.Organizations.GetRoleAsync(manager.Organization, user, cancellationToken);
        return user is not null && role is not null
            ? user
            : throw InvitationErrors.Coded(AppErrorType.NotFound, "The person is not a member of the organization.", IdentityErrorCodeConstants.UserNotMember).ToException();
    }

    private static InvitationDto ToDto(IdentityInvitation invitation, InvitationService invitations) => new()
    {
        Id = invitation.Id,
        Role = invitation.Role,
        Status = invitations.StatusOf(invitation).ToString().ToLowerInvariant(),
        PhoneNumber = invitation.PhoneNumber,
        Email = invitation.Email,
        CreatedAt = invitation.CreatedAt,
        ExpiresAt = invitation.ExpiresAt,
        AcceptedAt = invitation.AcceptedAt,
        AcceptedByUserId = invitation.AcceptedByUserId,
        TrustedContactUserId = invitation.TrustedContactUserId,
    };

    /// <summary>The error a refused trusted contact answers with: <c>404</c> while they are off, else <c>400</c>.</summary>
    private static AppError TrustedContactErrorOf(IdentityResult refusal)
    {
        var error = refusal.Errors[0];
        return InvitationErrors.Coded(
            error.Code == TrustedContactErrorCodeConstants.TrustedContactsOff ? AppErrorType.NotFound : AppErrorType.Validation,
            error.Description,
            error.Code);
    }

    /// <summary>The organization an owner or admin acts on, with the services and the caller.</summary>
    private sealed record Manager(OrganizationService Organizations, IdentityOrganization Organization, IdentityUser Caller);
}

using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

/// <summary>Maps the organization management API: members and, with invitations registered, invitation links.</summary>
public static class OrganizationEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps, under <paramref name="endpoints"/> (<c>/api/organizations</c> in the service), for the organization's owners and
    /// admins: <c>GET {organizationId}/members</c>; <c>PUT {organizationId}/members/{userId}</c> with <c>{ role }</c>;
    /// <c>DELETE {organizationId}/members/{userId}</c>; and, with invitations registered, <c>POST {organizationId}/invitations</c>
    /// with <c>{ role, phoneNumber?, email? }</c> (the answer carries the link once), <c>GET {organizationId}/invitations</c> and
    /// <c>DELETE {organizationId}/invitations/{invitationId}</c>.
    /// </summary>
    /// <remarks>
    ///   - a signed-in person outside the organization gets <c>404</c> (<c>OrganizationNotFound</c>), a member who is neither
    ///     owner nor admin <c>403</c> (<c>OrganizationManagerRequired</c>)
    ///   - only an owner invites, promotes, demotes or removes an owner (<c>403</c>, <c>OrganizationOwnerRequired</c>); the
    ///     last owner can be neither demoted nor removed (<c>409</c>, <c>LastOrganizationOwner</c>)
    ///   - a role change or removal rotates the member's security stamp, ending their sessions
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

        return endpoints;
    }

    private static async Task<IResult> ListMembersAsync(Guid organizationId, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var manager = await ManagerOfAsync(organizationId, principal, http, cancellationToken);
        var accounts = http.RequestServices.GetRequiredService<UserAccountService>();
        var members = new List<OrganizationMemberDto>();
        foreach (var membership in await manager.Organizations.ListMembersAsync(manager.Organization, cancellationToken))
        {
            if (await accounts.FindByIdAsync(membership.UserId, cancellationToken) is not { } user)
                continue;

            members.Add(new OrganizationMemberDto
            {
                UserId = user.Id,
                DisplayName = user.DisplayName,
                UserName = user.UserName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                Role = membership.Role,
                JoinedAt = membership.CreatedAt,
            });
        }

        return Results.Ok(ApiResponse<IReadOnlyList<OrganizationMemberDto>>.Ok(members));
    }

    private static async Task<IResult> ChangeRoleAsync(Guid organizationId, Guid userId, MemberRoleApiRequest request, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var manager = await ManagerOfAsync(organizationId, principal, http, cancellationToken);
        var member = await MemberOfAsync(manager, userId, http, cancellationToken);
        if ((manager.Organizations.IsOwnerRole(member.Role) || manager.Organizations.IsOwnerRole(request.Role)) && !manager.IsOwner)
            throw OwnerRequired().ToException();

        var changed = await manager.Organizations.ChangeRoleAsync(manager.Organization, member.User, request.Role, cancellationToken);
        return changed.IsSuccess ? Results.NoContent() : throw InvitationErrors.Of(changed).ToException();
    }

    private static async Task<IResult> RemoveMemberAsync(Guid organizationId, Guid userId, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var manager = await ManagerOfAsync(organizationId, principal, http, cancellationToken);
        var member = await MemberOfAsync(manager, userId, http, cancellationToken);
        if (manager.Organizations.IsOwnerRole(member.Role) && !manager.IsOwner)
            throw OwnerRequired().ToException();

        var removed = await manager.Organizations.RemoveMemberAsync(manager.Organization, member.User, cancellationToken);
        return removed.IsSuccess ? Results.NoContent() : throw InvitationErrors.Of(removed).ToException();
    }

    private static async Task<IResult> CreateInvitationAsync(Guid organizationId, InvitationApiRequest request, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var manager = await ManagerOfAsync(organizationId, principal, http, cancellationToken);
        if (manager.Organizations.IsOwnerRole(request.Role) && !manager.IsOwner)
            throw OwnerRequired().ToException();

        var invitations = http.RequestServices.GetRequiredService<InvitationService>();
        var created = await invitations.CreateAsync(manager.Organization, manager.Caller, request.Role, request.PhoneNumber, request.Email, cancellationToken);
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

        return new Manager(organizations, organization, caller, organizations.IsOwnerRole(role));
    }

    /// <summary>The member <paramref name="userId"/> with their role; <c>404</c> when they are not one.</summary>
    private static async Task<(IdentityUser User, string Role)> MemberOfAsync(Manager manager, Guid userId, HttpContext http, CancellationToken cancellationToken)
    {
        var user = await http.RequestServices.GetRequiredService<UserAccountService>().FindByIdAsync(userId, cancellationToken);
        var role = user is null ? null : await manager.Organizations.GetRoleAsync(manager.Organization, user, cancellationToken);
        return user is not null && role is not null
            ? (user, role)
            : throw InvitationErrors.Coded(AppErrorType.NotFound, "The person is not a member of the organization.", IdentityErrorCodeConstants.UserNotMember).ToException();
    }

    private static AppError OwnerRequired()
        => InvitationErrors.Coded(AppErrorType.Forbidden, "Only an owner invites, promotes, demotes or removes an owner.", IdentityErrorCodeConstants.OrganizationOwnerRequired);

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
    };

    /// <summary>The organization a manager acts on, with the services and whether they own it.</summary>
    private sealed record Manager(OrganizationService Organizations, IdentityOrganization Organization, IdentityUser Caller, bool IsOwner);
}

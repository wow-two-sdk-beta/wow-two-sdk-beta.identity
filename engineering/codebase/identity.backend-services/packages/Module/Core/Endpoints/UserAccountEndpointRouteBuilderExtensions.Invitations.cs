using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

public static partial class UserAccountEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps <c>GET invitations/{token}</c>, the public preview of an invitation link (<c>404</c> when it names none), and
    /// <c>POST invitations/{token}/accept</c>, which adds the signed-in person to its organization with its role; joining
    /// ends their sessions, so the browser's cookie is cleared and they sign in again with the new role.
    /// </summary>
    private static void MapInvitationEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/invitations/{token}", PreviewInvitationAsync);
        endpoints.MapPost("/invitations/{token}/accept", AcceptInvitationAsync).RequireAuthorization();
    }

    private static async Task<IResult> PreviewInvitationAsync(string token, HttpContext http, CancellationToken cancellationToken)
    {
        var lookup = await http.RequestServices.GetRequiredService<InvitationService>().FindAsync(token, cancellationToken)
            ?? throw InvitationErrors.Of(InvitationService.NotFound()).ToException();
        var invitation = lookup.Invitation;
        return Results.Ok(ApiResponse<InvitationPreviewDto>.Ok(new InvitationPreviewDto
        {
            OrganizationName = lookup.Organization.Name,
            Role = invitation.Role,
            Status = lookup.Status.ToString().ToLowerInvariant(),
            PhoneNumberHint = PersonalDataMasks.MaskSubject(invitation.PhoneNumber),
            EmailHint = PersonalDataMasks.MaskSubject(invitation.Email),
            ExpiresAt = invitation.ExpiresAt,
            TrustedContactHint = await TrustedContactHintAsync(http.RequestServices, invitation, cancellationToken),
        }));
    }

    /// <summary>The invitation's trusted contact, masked, while trusted contacts are on; null otherwise.</summary>
    private static async Task<string?> TrustedContactHintAsync(IServiceProvider services, IdentityInvitation invitation, CancellationToken cancellationToken)
    {
        if (invitation.TrustedContactUserId is not { } contactId || services.GetService<TrustedContactService>() is not { IsEnabled: true })
            return null;

        var contact = await services.GetRequiredService<UserAccountService>().FindByIdAsync(contactId, cancellationToken);
        return contact is null
            ? null
            : PersonalDataMasks.MaskName(contact.DisplayName) ?? PersonalDataMasks.MaskSubject(contact.IsPhoneNumberConfirmed ? contact.PhoneNumber : null);
    }

    private static async Task<IResult> AcceptInvitationAsync(string token, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var invitations = services.GetRequiredService<InvitationService>();
        var user = await FindAsync(services, principal.FindFirst(services.GetRequiredService<IdentityCoreOptions>().Claims.UserIdClaimType)?.Value, cancellationToken)
            ?? throw AppErrorFactory.Unauthorized("The account no longer exists.").ToException();
        var lookup = await invitations.FindAsync(token, cancellationToken)
            ?? throw InvitationErrors.Of(InvitationService.NotFound()).ToException();
        var redeemed = await invitations.RedeemAsync(lookup.Invitation, user, cancellationToken);
        if (!redeemed.IsSuccess)
            throw InvitationErrors.Of(redeemed).ToException();

        // Joining is a role change, which ended the person's sessions: clear this browser's cookie so it signs in again.
        if (await HasCookieSchemeAsync(services))
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return Results.NoContent();
    }

    /// <summary>
    /// The pending invitation <paramref name="token"/> names, checked against the identifier signing up; null without a token.
    /// A token naming none, or an invitation that is not pending or names another identifier, is refused.
    /// </summary>
    internal static async Task<IdentityInvitation?> InvitationOfAsync(IServiceProvider services, string? token, string? email, string? phoneNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var invitations = services.GetService<InvitationService>()
            ?? throw InvitationErrors.Of(InvitationService.NotFound()).ToException();
        var lookup = await invitations.FindAsync(token, cancellationToken)
            ?? throw InvitationErrors.Of(InvitationService.NotFound()).ToException();
        var check = invitations.Check(lookup.Invitation, email, phoneNumber);
        return check.IsSuccess ? lookup.Invitation : throw InvitationErrors.Of(check).ToException();
    }
}

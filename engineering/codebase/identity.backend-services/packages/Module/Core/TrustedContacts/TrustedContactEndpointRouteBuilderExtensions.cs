using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>Maps a person's trusted contacts and own phone onto the account API.</summary>
public static class TrustedContactEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps, under <paramref name="endpoints"/> (<c>/api/account</c> in the service), for the signed-in person:
    /// <c>GET manage/trusted-contacts</c> (<c>{ canRevoke, items }</c>), <c>DELETE manage/trusted-contacts/{userId}</c>,
    /// <c>GET manage/trusted-by</c>, <c>DELETE manage/trusted-by/{userId}</c>, <c>GET manage/phone</c> and
    /// <c>POST manage/phone/telegram-link</c>. The sign-in and sign-up routes through a contact map with phone sign-in and
    /// the invitation sign-up (<c>MapPhoneSignInEndpoints</c>, <c>MapInviteSignUpEndpoints</c>).
    /// </summary>
    /// <remarks>
    ///   - revoking a contact needs a session signed in with the account's own phone (<c>403</c> <c>MainPhoneRequired</c>);
    ///     it ends every session started through the contact; <c>404</c> <c>TrustedContactNotFound</c> for none
    ///   - a contact leaves an account from any session, unless they are the last contact of an account with no own phone
    ///     (<c>409</c> <c>LastTrustedContact</c>)
    ///   - the <c>add-phone</c> link adds a first phone from any session; changing an existing one needs an own-phone session
    ///     (<c>403</c> <c>MainPhoneRequired</c>); <c>404</c> <c>PhoneSignInUnavailable</c> while the bot is off,
    ///     <c>429</c> <c>PhoneCodeRateLimited</c> moments after another link from the same address
    ///   - failures carry their code in the problem's <c>identityCode</c>
    /// </remarks>
    /// <param name="endpoints">The account API's route group.</param>
    public static IEndpointRouteBuilder MapTrustedContactEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet("/manage/trusted-contacts", ListContactsAsync).RequireAuthorization();
        endpoints.MapDelete("/manage/trusted-contacts/{userId:guid}", RevokeContactAsync).RequireAuthorization();
        endpoints.MapGet("/manage/trusted-by", ListTrustedByAsync).RequireAuthorization();
        endpoints.MapDelete("/manage/trusted-by/{userId:guid}", LeaveAsync).RequireAuthorization();
        endpoints.MapGet("/manage/phone", PhoneAsync).RequireAuthorization();
        endpoints.MapPost("/manage/phone/telegram-link", PhoneLinkAsync).RequireAuthorization().LimitCredentials(endpoints);
        return endpoints;
    }

    private static async Task<IResult> ListContactsAsync(ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, sessionId) = await CallerAsync(principal, http, cancellationToken);
        var services = http.RequestServices;
        var contacts = await services.GetRequiredService<TrustedContactService>().ListContactsAsync(user, cancellationToken);
        var canRevoke = await services.GetRequiredService<OwnPhoneService>().IsOwnPhoneSessionAsync(user, sessionId, cancellationToken);
        return Results.Ok(ApiResponse<TrustedContactsDto>.Ok(new TrustedContactsDto
        {
            CanRevoke = canRevoke,
            Items =
            [
                .. contacts.Select(contact => new TrustedContactDto
                {
                    UserId = contact.UserId,
                    DisplayName = contact.DisplayName,
                    PhoneHint = contact.PhoneHint,
                    IsDeliverable = contact.IsDeliverable,
                    CreatedAt = contact.CreatedAt,
                }),
            ],
        }));
    }

    private static async Task<IResult> RevokeContactAsync(Guid userId, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, sessionId) = await CallerAsync(principal, http, cancellationToken);
        var services = http.RequestServices;
        if (!await services.GetRequiredService<OwnPhoneService>().IsOwnPhoneSessionAsync(user, sessionId, cancellationToken))
            throw MainPhoneRequired().ToException();

        return await services.GetRequiredService<TrustedContactService>().RevokeAsync(user, userId, cancellationToken)
            ? Results.NoContent()
            : throw NotFound().ToException();
    }

    private static async Task<IResult> ListTrustedByAsync(ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, _) = await CallerAsync(principal, http, cancellationToken);
        var trusting = await http.RequestServices.GetRequiredService<TrustedContactService>().ListTrustingAsync(user, cancellationToken);
        return Results.Ok(ApiResponse<IReadOnlyList<TrustedByDto>>.Ok(
        [
            .. trusting.Select(account => new TrustedByDto
            {
                UserId = account.UserId,
                DisplayName = account.DisplayName,
                CreatedAt = account.CreatedAt,
                CanLeave = account.CanLeave,
            }),
        ]));
    }

    private static async Task<IResult> LeaveAsync(Guid userId, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, _) = await CallerAsync(principal, http, cancellationToken);
        return await http.RequestServices.GetRequiredService<TrustedContactService>().LeaveAsync(user, userId, cancellationToken) switch
        {
            TrustedContactLeaveStatus.Left => Results.NoContent(),
            TrustedContactLeaveStatus.LastContact => throw InvitationErrors.Coded(
                AppErrorType.Conflict,
                "You are this account's last trusted contact and it has no phone of its own; it would lose its way in.",
                TrustedContactErrorCodeConstants.LastTrustedContact).ToException(),
            _ => throw NotFound().ToException(),
        };
    }

    private static async Task<IResult> PhoneAsync(ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, sessionId) = await CallerAsync(principal, http, cancellationToken);
        var canChange = await http.RequestServices.GetRequiredService<OwnPhoneService>().CanChangeAsync(user, sessionId, cancellationToken);
        return Results.Ok(ApiResponse<OwnPhoneDto>.Ok(new OwnPhoneDto
        {
            PhoneHint = PersonalDataMasks.MaskSubject(user.PhoneNumber),
            IsConfirmed = OwnPhoneService.HasOwnPhone(user),
            CanChange = canChange,
        }));
    }

    private static async Task<IResult> PhoneLinkAsync(ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, sessionId) = await CallerAsync(principal, http, cancellationToken);
        var link = await http.RequestServices.GetRequiredService<OwnPhoneService>().CreateLinkAsync(user, sessionId, http.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return link.Status switch
        {
            OwnPhoneLinkStatus.Created => Results.Ok(ApiResponse<OwnPhoneLinkDto>.Ok(new OwnPhoneLinkDto { TelegramLinkUrl = link.TelegramLinkUrl! })),
            OwnPhoneLinkStatus.MainPhoneRequired => throw MainPhoneRequired().ToException(),
            OwnPhoneLinkStatus.RateLimited => throw InvitationErrors.Coded(AppErrorType.TooManyRequests, "A link went out moments ago; wait before asking again.", PhoneSignInErrorCodeConstants.PhoneCodeRateLimited).ToException(),
            _ => throw InvitationErrors.Coded(AppErrorType.NotFound, "Phone sign-in is off on this instance.", PhoneSignInErrorCodeConstants.PhoneSignInUnavailable).ToException(),
        };
    }

    /// <summary>The signed-in person and their cookie's session id (null for a bearer token).</summary>
    private static async Task<(IdentityUser User, Guid? SessionId)> CallerAsync(ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var claims = services.GetRequiredService<IdentityCoreOptions>().Claims;
        var user = Guid.TryParse(principal.FindFirst(claims.UserIdClaimType)?.Value, out var userId)
            ? await services.GetRequiredService<UserAccountService>().FindByIdAsync(userId, cancellationToken)
            : null;
        if (user is null)
            throw AppErrorFactory.Unauthorized("The account no longer exists.").ToException();

        return (user, Guid.TryParse(principal.FindFirst(claims.SessionIdClaimType)?.Value, out var sessionId) ? sessionId : null);
    }

    private static AppError MainPhoneRequired()
        => InvitationErrors.Coded(AppErrorType.Forbidden, "Sign in with your own phone number to do this.", TrustedContactErrorCodeConstants.MainPhoneRequired);

    private static AppError NotFound()
        => InvitationErrors.Coded(AppErrorType.NotFound, "No such trusted contact.", TrustedContactErrorCodeConstants.TrustedContactNotFound);
}

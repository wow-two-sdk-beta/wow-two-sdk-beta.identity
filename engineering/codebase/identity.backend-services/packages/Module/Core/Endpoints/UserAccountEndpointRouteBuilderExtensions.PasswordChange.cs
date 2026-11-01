using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Factories;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>The password change routes of the account API: the confirmation, the change, and the change by link.</summary>
public static partial class UserAccountEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps <c>manage/password/confirmation</c> and <c>manage/password</c>, which a signed-in person calls, or a person
    /// holding the ticket a sign-in answered with when their password breaks the current rules; and <c>password/change</c>,
    /// which anyone holding a link's token calls. None of them demands authorization up front: each decides who is asking.
    /// </summary>
    private static void MapPasswordChangeEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/manage/password/confirmation", PasswordChangeConfirmationAsync);
        endpoints.MapPost("/manage/password", ChangePasswordAsync);
        endpoints.MapPost("/password/change", ChangePasswordByLinkAsync);
    }

    private static async Task<IResult> PasswordChangeConfirmationAsync(
        PasswordChangeConfirmationApiRequest? request,
        ClaimsPrincipal principal,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var change = http.RequestServices.GetRequiredService<UserPasswordChangeService>();
        var (user, _) = await PasswordChangeSubjectAsync(request?.Ticket, principal, http, change, cancellationToken);
        var sent = await change.SendConfirmationAsync(user, cancellationToken: cancellationToken);
        return sent.Status switch
        {
            PasswordChangeConfirmationStatus.NotRequired or PasswordChangeConfirmationStatus.Sent
                => Results.Ok(ApiResponse<PasswordChangeConfirmationDto>.Ok(new PasswordChangeConfirmationDto
                {
                    Confirmation = sent.Confirmation.ToString(),
                    IsSent = sent.IsSent,
                })),
            PasswordChangeConfirmationStatus.RateLimited
                => throw CodedError(AppErrorType.TooManyRequests, "A code was sent moments ago; wait before asking again.", PasswordChangeErrorCodeConstants.RateLimited).ToException(),
            PasswordChangeConfirmationStatus.NoVerifiedChannel
                => throw CodedError(AppErrorType.BusinessRule, "Confirm an email address or link a messenger before changing the password.", PasswordChangeErrorCodeConstants.NoVerifiedChannel).ToException(),
            _ => throw CodedError(AppErrorType.ExternalUnavailable, "The code could not be delivered; try again later.", PasswordChangeErrorCodeConstants.DeliveryFailed).ToException(),
        };
    }

    private static async Task<IResult> ChangePasswordAsync(
        PasswordChangeApiRequest request,
        ClaimsPrincipal principal,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var change = services.GetRequiredService<UserPasswordChangeService>();
        var (user, isTicketed) = await PasswordChangeSubjectAsync(request.Ticket, principal, http, change, cancellationToken);

        // Read before the change: the change rotates the stamp that this cookie still carries.
        var session = isTicketed ? null : SessionOf(services, principal);
        var cookie = session is not null && await HasCookieSchemeAsync(services)
            ? await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme)
            : null;

        var changed = await change.ChangeAsync(user, request.CurrentPassword, request.NewPassword, request.Code, isTicketed, session?.SessionId, cancellationToken);
        if (!changed.IsSuccess)
            throw ChangeFailure(changed);

        if (session is { } signedIn && cookie is not null)
        {
            if (changed.IsSessionKept)
                await ReissueCookieAsync(services, user, principal, signedIn.SessionId, cookie, http, cancellationToken);
            else
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> ChangePasswordByLinkAsync(PasswordChangeLinkApiRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var changed = await http.RequestServices.GetRequiredService<UserPasswordChangeService>()
            .ChangeByLinkAsync(request.Token, request.NewPassword, cancellationToken);
        return changed.IsSuccess ? Results.NoContent() : throw ChangeFailure(changed);
    }

    /// <summary>The sign-in's answer for a password that matched but breaks the current rules: the way to change it, no session.</summary>
    private static IResult PasswordChangeRequired(SignInResult result, HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        return Results.Ok(ApiResponse<PasswordChangeRequiredDto>.Ok(new PasswordChangeRequiredDto
        {
            Confirmation = http.RequestServices.GetRequiredService<IdentityCoreOptions>().Password.Change.Confirmation.ToString(),
            Ticket = result.PasswordChangeTicket!,
        }));
    }

    /// <summary>The person asking: the signed-in one, else the one a live forced-change ticket names.</summary>
    private static async Task<(IdentityUser User, bool IsTicketed)> PasswordChangeSubjectAsync(
        string? ticket,
        ClaimsPrincipal principal,
        HttpContext http,
        UserPasswordChangeService change,
        CancellationToken cancellationToken)
    {
        if (principal.Identity?.IsAuthenticated == true)
            return (await CurrentUserAsync(principal, http, cancellationToken), false);

        if (!string.IsNullOrWhiteSpace(ticket) && await change.RedeemRequiredChangeTicketAsync(ticket, cancellationToken) is { } user)
            return (user, true);

        throw AppErrorFactory.Unauthorized("Sign in, or use the ticket the sign-in gave you, to change the password.").ToException();
    }

    private static Exception ChangeFailure(PasswordChangeResult changed)
        => changed.Errors.Count > 0 && changed.Errors[0].Code == PasswordChangeErrorCodeConstants.LinkRequired
            ? CodedError(AppErrorType.Forbidden, changed.Errors[0].Description, PasswordChangeErrorCodeConstants.LinkRequired).ToException()
            : changed.ToIdentityResult().ToValidationError().ToException();

    private static async Task<bool> HasCookieSchemeAsync(IServiceProvider services)
        => await services.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync(CookieAuthenticationDefaults.AuthenticationScheme) is not null;

    /// <summary>
    /// Issues the cookie again for the session a change kept: the new security stamp, the same <c>sid</c> and sign-in
    /// methods, the expiry of the cookie it replaces.
    /// </summary>
    private static async Task ReissueCookieAsync(
        IServiceProvider services,
        IdentityUser user,
        ClaimsPrincipal presented,
        Guid sessionId,
        AuthenticateResult cookie,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var fresh = await services.GetRequiredService<UserClaimsPrincipalFactory>().CreateAsync(user, cancellationToken: cancellationToken);
        if (fresh.Identity is ClaimsIdentity identity)
        {
            foreach (var method in presented.FindAll(AuthenticationMethodConstants.ClaimType))
                identity.AddClaim(new Claim(AuthenticationMethodConstants.ClaimType, method.Value));

            identity.AddClaim(new Claim(services.GetRequiredService<IdentityCoreOptions>().Claims.SessionIdClaimType, sessionId.ToString()));
        }

        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            fresh,
            new AuthenticationProperties { ExpiresUtc = cookie.Properties?.ExpiresUtc });
    }
}

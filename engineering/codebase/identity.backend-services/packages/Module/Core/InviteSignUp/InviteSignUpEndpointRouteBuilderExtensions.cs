using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.ProfilePictures;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;
using WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.InviteSignUp;

/// <summary>Maps sign-up from an invitation, with the phone the bot verifies, onto the account API.</summary>
public static class InviteSignUpEndpointRouteBuilderExtensions
{
    /// <summary>The HttpOnly cookie binding a started sign-up to the browser: <c>{id}.{secret}</c>.</summary>
    public const string CookieName = "__Host-identity-sign-up";

    private const int FormOverheadBytes = 64 * 1024;

    /// <summary>The code of a <c>verification</c> other than <c>telegram</c> or <c>trusted-contact</c>.</summary>
    private const string InvalidVerificationCode = "InvalidSignUpVerification";

    /// <summary>
    /// Maps <c>invitations/{token}/sign-up</c> and <c>invitations/sign-up/status</c> under <paramref name="endpoints"/>, the
    /// account API's group (<c>/api/account</c> in the service).
    /// </summary>
    /// <remarks>
    ///   - <c>POST invitations/{token}/sign-up</c> takes <c>multipart/form-data</c> with <c>displayName</c> and a <c>picture</c>
    ///     file, as <c>Identity:SignUp:Profile</c> asks, and answers <c>{ telegramLinkUrl, expiresAt }</c>, setting the
    ///     <see cref="CookieName"/> cookie; <c>400</c> for a name or picture the rules refuse (<c>DisplayNameRequired</c>,
    ///     <c>PictureUnsupportedType</c>, …), <c>404</c>/<c>409</c> for an unknown or spent invitation, <c>404</c>
    ///     <c>PhoneSignInUnavailable</c> while the bot is off, <c>429</c> <c>PhoneCodeRateLimited</c>; it takes the credential
    ///     rate-limit policy
    ///   - <c>GET invitations/sign-up/status</c> reads the cookie and answers <c>{ status, failureCode, isSignedIn }</c>:
    ///     <c>pending</c>, <c>completed</c> (the first such answer signs the browser in with a cookie session, <c>amr</c>
    ///     <c>otp</c>, sign-in log method <c>phone_contact</c>), <c>failed</c> or <c>expired</c>; <c>404</c>
    ///     <c>SignUpVerificationNotFound</c> without a matching cookie. A finished sign-up clears the cookie.
    ///   - with trusted contacts registered, the form's <c>verification</c> is <c>telegram</c> (the default) or
    ///     <c>trusted-contact</c>: the latter sends a code to the invitation's contact and answers
    ///     <c>{ isCodeSentToTrustedContact: true, expiresAt }</c> with no link (<c>404</c> <c>TrustedContactsOff</c>,
    ///     <c>400</c> <c>NoTrustedContact</c>, <c>409</c> <c>TrustedContactUnavailable</c>); <c>POST invitations/sign-up/verify</c>
    ///     takes <c>{ code, isPersistent }</c> with the cookie and answers <c>204</c> with the session cookie: the person is
    ///     created with no phone, joins and has the contact recorded, in one transaction (sign-in log method
    ///     <c>trusted_contact_code</c>, the session naming the contact); <c>401</c> <c>InvalidPhoneCode</c> for a wrong code,
    ///     <c>404</c> <c>SignUpVerificationNotFound</c> without a waiting sign-up; it takes the credential rate-limit policy
    /// </remarks>
    /// <param name="endpoints">The account API's route group.</param>
    public static IEndpointRouteBuilder MapInviteSignUpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost("/invitations/{token}/sign-up", StartAsync).LimitCredentials(endpoints);
        endpoints.MapGet("/invitations/sign-up/status", StatusAsync);
        if (endpoints.ServiceProvider.GetService<IServiceProviderIsService>()?.IsService(typeof(TrustedContactService)) == true)
            endpoints.MapPost("/invitations/sign-up/verify", VerifyAsync).LimitCredentials(endpoints);
        return endpoints;
    }

    private static async Task<IResult> StartAsync(string token, string? clientId, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var rules = services.GetRequiredService<IOptionsMonitor<ProfilePictureOptions>>().CurrentValue;
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            limit.MaxRequestBodySize = rules.MaxUploadBytes + FormOverheadBytes;

        string? displayName = null;
        byte[]? picture = null;
        var verification = InviteSignUpVerification.Telegram;
        if (http.Request.HasFormContentType)
        {
            IFormCollection form;
            try
            {
                form = await http.Request.ReadFormAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is BadHttpRequestException or InvalidDataException)
            {
                throw Coded(AppErrorType.Validation, $"The picture must be at most {rules.MaxUploadBytes / (1024 * 1024)} MB.", IdentityErrorCodeConstants.PictureTooLarge).ToException();
            }

            displayName = form["displayName"].FirstOrDefault();
            verification = form["verification"].FirstOrDefault()?.Trim().ToLowerInvariant() switch
            {
                null or "" or "telegram" => InviteSignUpVerification.Telegram,
                "trusted-contact" => InviteSignUpVerification.TrustedContact,
                _ => throw Coded(AppErrorType.Validation, "Verify with 'telegram' or 'trusted-contact'.", InvalidVerificationCode).ToException(),
            };
            if (form.Files.GetFile("picture") is { Length: > 0 } file)
                picture = file.Length > rules.MaxUploadBytes ? new byte[rules.MaxUploadBytes + 1] : await ReadAsync(file, cancellationToken);
        }

        var started = await services.GetRequiredService<InviteSignUpService>().StartAsync(
            new InviteSignUpStartRequest
            {
                Token = token,
                DisplayName = displayName,
                Picture = picture,
                ClientId = clientId,
                Origin = http.Connection.RemoteIpAddress?.ToString(),
                Verification = verification,
            },
            cancellationToken);
        switch (started.Status)
        {
            case InviteSignUpStartStatus.Started:
                http.Response.Cookies.Append(
                    CookieName,
                    started.VerificationId!.Value.ToString("N", CultureInfo.InvariantCulture) + "." + started.BrowserSecret,
                    new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/", Expires = started.ExpiresAt, IsEssential = true });
                return Results.Ok(ApiResponse<InviteSignUpStartedDto>.Ok(new InviteSignUpStartedDto
                {
                    TelegramLinkUrl = started.TelegramLinkUrl,
                    IsCodeSentToTrustedContact = started.IsCodeSentToTrustedContact,
                    ExpiresAt = started.ExpiresAt!.Value,
                }));
            case InviteSignUpStartStatus.TrustedContactRefused:
                throw TrustedContactError(started.Refusal!).ToException();
            case InviteSignUpStartStatus.InvitationRefused:
                throw InvitationErrors.Of(started.Refusal!).ToException();
            case InviteSignUpStartStatus.Invalid:
                throw Coded(AppErrorType.Validation, started.Refusal!.Errors[0].Description, started.Refusal.Errors[0].Code).ToException();
            case InviteSignUpStartStatus.RateLimited:
                throw Coded(AppErrorType.TooManyRequests, "A link went out moments ago; wait before asking again.", PhoneSignInErrorCodeConstants.PhoneCodeRateLimited).ToException();
            default:
                throw Coded(AppErrorType.NotFound, "Phone sign-in is off on this instance.", PhoneSignInErrorCodeConstants.PhoneSignInUnavailable).ToException();
        }
    }

    private static async Task<IResult> StatusAsync(string? clientId, HttpContext http, CancellationToken cancellationToken)
    {
        if (!TryReadCookie(http, out var signUpId, out var secret))
            throw NotFound().ToException();

        var services = http.RequestServices;
        var poll = await services.GetRequiredService<InviteSignUpService>().PollAsync(signUpId, secret, cancellationToken);
        var isSignedIn = http.User.Identity?.IsAuthenticated == true;
        switch (poll.State)
        {
            case InviteSignUpState.NotFound:
                http.Response.Cookies.Delete(CookieName, new CookieOptions { Secure = true, Path = "/" });
                throw NotFound().ToException();
            case InviteSignUpState.Pending:
                return Status("pending");
            case InviteSignUpState.Completed when poll.SignInAs is { } person:
                isSignedIn = await SignInAsync(person, clientId ?? poll.ClientId, http, cancellationToken);
                break;
        }

        http.Response.Cookies.Delete(CookieName, new CookieOptions { Secure = true, Path = "/" });
        return poll.State switch
        {
            InviteSignUpState.Completed => Status("completed", isSignedIn: isSignedIn),
            InviteSignUpState.Failed => Status("failed", poll.FailureCode),
            _ => Status("expired"),
        };
    }

    private static async Task<IResult> VerifyAsync(InviteSignUpVerifyApiRequest request, string? clientId, HttpContext http, CancellationToken cancellationToken)
    {
        if (!TryReadCookie(http, out var signUpId, out var secret))
            throw NotFound().ToException();

        var verified = await http.RequestServices.GetRequiredService<InviteSignUpService>().VerifyThroughContactAsync(signUpId, secret, request.Code, cancellationToken);
        switch (verified.Status)
        {
            case InviteSignUpVerifyStatus.InvalidCode:
                throw Coded(AppErrorType.Unauthorized, "The code is invalid or has expired.", IdentityErrorCodeConstants.InvalidPhoneCode).ToException();
            case InviteSignUpVerifyStatus.NotFound:
                throw NotFound().ToException();
            case InviteSignUpVerifyStatus.Expired:
                http.Response.Cookies.Delete(CookieName, new CookieOptions { Secure = true, Path = "/" });
                throw Coded(AppErrorType.Conflict, "The sign-up has expired; start again from the invitation link.", IdentityErrorCodeConstants.SignUpVerificationNotFound).ToException();
            case InviteSignUpVerifyStatus.Failed:
                http.Response.Cookies.Delete(CookieName, new CookieOptions { Secure = true, Path = "/" });
                throw InvitationErrors.Of(IdentityUserExtensions.Failure(verified.FailureCode ?? IdentityErrorCodeConstants.InvitationNotFound, "The invitation can no longer be used; ask for a new link.")).ToException();
        }

        http.Response.Cookies.Delete(CookieName, new CookieOptions { Secure = true, Path = "/" });
        var isSignedIn = await SignInAsync(verified.SignInAs!, clientId ?? verified.ClientId, http, cancellationToken, verified.TrustedContactUserId, request.IsPersistent);
        return isSignedIn
            ? Results.NoContent()
            : throw Coded(AppErrorType.Unauthorized, "The account was created; sign in to continue.", IdentityErrorCodeConstants.SignInFailed).ToException();
    }

    /// <summary>
    /// Signs the browser in as <paramref name="person"/> with a cookie session; false when the person must sign in themselves,
    /// such as with a second factor. A sign-up through a trusted contact logs <c>trusted_contact_code</c> and marks the session.
    /// </summary>
    private static async Task<bool> SignInAsync(
        IdentityUser person, string? clientId, HttpContext http, CancellationToken cancellationToken, Guid? trustedContactUserId = null, bool isPersistent = false)
    {
        var services = http.RequestServices;
        if (services.GetService<SignInLogService>() is { } log)
        {
            if (!string.IsNullOrWhiteSpace(clientId))
                log.ClientId = clientId;
            log.TrustedContactUserId = trustedContactUserId;
        }

        var method = trustedContactUserId is null ? SignInMethodConstants.PhoneContact : SignInMethodConstants.TrustedContactCode;
        var result = await services.GetRequiredService<SignInService>().SignInAsync(person, [AuthenticationMethodConstants.OneTimePassword], method, cancellationToken);
        if (!result.IsSuccess || result.Principal is not { } principal)
            return false;

        var properties = new AuthenticationProperties();
        if (services.GetService<UserSessionService>() is { } sessions && principal.Identity is ClaimsIdentity identity)
        {
            var methods = principal.FindAll(AuthenticationMethodConstants.ClaimType).Select(claim => claim.Value);
            var session = await sessions.StartAsync(person, methods, result.SignInLogEntryId, isPersistent, trustedContactUserId, cancellationToken);
            identity.AddClaim(new Claim(services.GetRequiredService<IdentityCoreOptions>().Claims.SessionIdClaimType, session.Id.ToString()));
            properties.IssuedUtc = session.CreatedAt;
            properties.ExpiresUtc = session.ExpiresAt;
            properties.IsPersistent = sessions.IsCookiePersistent(isPersistent);
        }

        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
        return true;
    }

    /// <summary>The error a sign-up through a trusted contact is refused with: <c>404</c> while they are off, <c>400</c> with none named, <c>409</c> when unreachable.</summary>
    private static AppError TrustedContactError(IdentityResult refusal)
    {
        var error = refusal.Errors[0];
        var type = error.Code switch
        {
            TrustedContactErrorCodeConstants.TrustedContactsOff => AppErrorType.NotFound,
            TrustedContactErrorCodeConstants.NoTrustedContact => AppErrorType.Validation,
            _ => AppErrorType.Conflict,
        };
        return Coded(type, error.Description, error.Code);
    }

    private static bool TryReadCookie(HttpContext http, out Guid signUpId, out string secret)
    {
        signUpId = Guid.Empty;
        secret = string.Empty;
        var value = http.Request.Cookies[CookieName];
        var dot = value?.IndexOf('.', StringComparison.Ordinal) ?? -1;
        if (value is null || dot <= 0 || !Guid.TryParseExact(value[..dot], "N", out signUpId))
            return false;

        secret = value[(dot + 1)..];
        return secret.Length > 0;
    }

    private static async Task<byte[]> ReadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static IResult Status(string status, string? failureCode = null, bool isSignedIn = false)
        => Results.Ok(ApiResponse<InviteSignUpStatusDto>.Ok(new InviteSignUpStatusDto { Status = status, FailureCode = failureCode, IsSignedIn = isSignedIn }));

    private static AppError NotFound()
        => Coded(AppErrorType.NotFound, "No sign-up is waiting in this browser; start again from the invitation link.", IdentityErrorCodeConstants.SignUpVerificationNotFound);

    private static AppError Coded(AppErrorType type, string message, string code) => InvitationErrors.Coded(type, message, code);
}

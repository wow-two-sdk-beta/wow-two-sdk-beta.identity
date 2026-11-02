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
using WoW.Two.Sdk.Beta.Be.Web.Contracts;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.InviteSignUp;

/// <summary>Maps sign-up from an invitation, with the phone the bot verifies, onto the account API.</summary>
public static class InviteSignUpEndpointRouteBuilderExtensions
{
    /// <summary>The HttpOnly cookie binding a started sign-up to the browser: <c>{id}.{secret}</c>.</summary>
    public const string CookieName = "__Host-identity-sign-up";

    private const int FormOverheadBytes = 64 * 1024;

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
    /// </remarks>
    /// <param name="endpoints">The account API's route group.</param>
    public static IEndpointRouteBuilder MapInviteSignUpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost("/invitations/{token}/sign-up", StartAsync).LimitCredentials(endpoints);
        endpoints.MapGet("/invitations/sign-up/status", StatusAsync);
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
            },
            cancellationToken);
        switch (started.Status)
        {
            case InviteSignUpStartStatus.Started:
                http.Response.Cookies.Append(
                    CookieName,
                    started.VerificationId!.Value.ToString("N", CultureInfo.InvariantCulture) + "." + started.BrowserSecret,
                    new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/", Expires = started.ExpiresAt, IsEssential = true });
                return Results.Ok(ApiResponse<InviteSignUpStartedDto>.Ok(new InviteSignUpStartedDto { TelegramLinkUrl = started.TelegramLinkUrl!, ExpiresAt = started.ExpiresAt!.Value }));
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

    /// <summary>Signs the browser in as <paramref name="person"/> with a cookie session; false when the person must sign in themselves, such as with a second factor.</summary>
    private static async Task<bool> SignInAsync(IdentityUser person, string? clientId, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        if (!string.IsNullOrWhiteSpace(clientId) && services.GetService<SignInLogService>() is { } log)
            log.ClientId = clientId;

        var result = await services.GetRequiredService<SignInService>().SignInAsync(person, [AuthenticationMethodConstants.OneTimePassword], SignInMethodConstants.PhoneContact, cancellationToken);
        if (!result.IsSuccess || result.Principal is not { } principal)
            return false;

        var properties = new AuthenticationProperties();
        if (services.GetService<UserSessionService>() is { } sessions && principal.Identity is ClaimsIdentity identity)
        {
            var methods = principal.FindAll(AuthenticationMethodConstants.ClaimType).Select(claim => claim.Value);
            var session = await sessions.StartAsync(person, methods, result.SignInLogEntryId, isPersistent: false, cancellationToken);
            identity.AddClaim(new Claim(services.GetRequiredService<IdentityCoreOptions>().Claims.SessionIdClaimType, session.Id.ToString()));
            properties.IssuedUtc = session.CreatedAt;
            properties.ExpiresUtc = session.ExpiresAt;
            properties.IsPersistent = sessions.IsCookiePersistent(isPersistent: false);
        }

        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
        return true;
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

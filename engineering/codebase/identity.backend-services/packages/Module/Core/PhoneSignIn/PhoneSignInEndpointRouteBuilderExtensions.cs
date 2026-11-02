using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;
using WoW.Two.Sdk.Beta.Be.Identity.Core.RefreshTokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;
using WoW.Two.Sdk.Beta.Be.Identity.Core.TwoFactor;
using WoW.Two.Sdk.Beta.Be.Identity.Jwt.Issuance;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Factories;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;

/// <summary>Maps sign-in by phone onto the account API.</summary>
public static class PhoneSignInEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps <c>phone-sign-in/send</c> and <c>phone-sign-in</c> under <paramref name="endpoints"/>, the account API's group
    /// (<c>/api/account</c> in the service). Both answer <c>404</c> while the bot is off.
    /// </summary>
    /// <remarks>
    ///   - <c>send</c> takes <c>{ phone }</c> and answers <c>{ isCodeSent, telegramLinkUrl }</c>: a linked number's code
    ///     goes to Telegram at once (<c>isCodeSent</c> true); any other number gets the start link to open, and the code
    ///     follows the shared contact. Whether an account holds the number changes neither answer, but the answer does tell
    ///     a number linked to the bot from one that is not; <c>400</c> for a malformed number or one outside the allowed
    ///     countries, <c>429</c> when a code or link went out moments ago for the number or address, or the number spent
    ///     its failure budget; failures carry their code in the problem's <c>identityCode</c>
    ///   - <c>phone-sign-in</c> takes <c>{ phone, code }</c> and signs in as <c>sign-in</c> does: a cookie with
    ///     <c>?useCookies=true</c> (<c>204</c>), else a bearer token pair; <c>amr</c> <c>otp</c>, sign-in log method
    ///     <c>phone_code</c>, optional <c>?clientId=</c>. A first sign-in creates the person while <c>Identity:SignUp:Mode</c>
    ///     is <c>Open</c> or the request carries a pending invitation's <c>invitationToken</c>, else <c>403</c> (message key
    ///     <c>SignUpClosed</c>); with a token the person, new or known, joins its organization with its role; a wrong code is
    ///     <c>401</c> (<c>InvalidPhoneCode</c>); a second factor answers <c>401</c> with the ticket for <c>sign-in/two-factor</c>;
    ///     a bearer sign-in on a cookie-only instance is <c>400</c> (<c>BearerSignInUnsupported</c>) and a locked-out person
    ///     <c>429</c> (<c>LockedOut</c>), both before the code is spent
    ///   - both routes take the per-address <see cref="AccountRateLimitPolicyConstants.Credentials"/> policy
    /// </remarks>
    /// <param name="endpoints">The account API's route group.</param>
    public static IEndpointRouteBuilder MapPhoneSignInEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost("/phone-sign-in/send", SendAsync).LimitCredentials(endpoints);
        endpoints.MapPost("/phone-sign-in", SignInAsync).LimitCredentials(endpoints);
        return endpoints;
    }

    private static async Task<IResult> SendAsync(PhoneSignInSendApiRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var phoneSignIn = ServiceOf(http);
        var result = await phoneSignIn.SendAsync(request.Phone, http.Connection.RemoteIpAddress?.ToString(), CultureInfo.CurrentUICulture, cancellationToken);
        return result.Status switch
        {
            PhoneSignInStatus.CodeSent => Results.Ok(ApiResponse<PhoneSignInSentDto>.Ok(new PhoneSignInSentDto { IsCodeSent = true })),
            PhoneSignInStatus.LinkRequired => Results.Ok(ApiResponse<PhoneSignInSentDto>.Ok(new PhoneSignInSentDto { IsCodeSent = false, TelegramLinkUrl = result.TelegramLinkUrl })),
            PhoneSignInStatus.RateLimited => throw CodedError(AppErrorType.TooManyRequests, "A code was sent moments ago; wait before asking again.", PhoneSignInErrorCodeConstants.PhoneCodeRateLimited).ToException(),
            _ => throw NumberError(result.Status).ToException(),
        };
    }

    private static async Task<IResult> SignInAsync(PhoneSignInApiRequest request, bool? useCookies, string? clientId, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var phoneSignIn = ServiceOf(http);
        if (useCookies != true && services.GetService<ITokenIssuer>() is null)
            throw CodedError(AppErrorType.Validation, "Bearer sign-in is not enabled on this instance; sign in with ?useCookies=true.", AccountApiErrorCodeConstants.BearerSignInUnsupported).ToException();
        if (!string.IsNullOrWhiteSpace(clientId) && services.GetService<SignInLogService>() is { } log)
            log.ClientId = clientId;

        var invitation = await UserAccountEndpointRouteBuilderExtensions.InvitationOfAsync(
            services, request.InvitationToken, email: null, PhoneNumberPolicy.Normalize(request.Phone), cancellationToken);
        var isSignUpAllowed = invitation is not null || services.GetService<IOptionsMonitor<SignUpOptions>>()?.CurrentValue.IsOpen == true;
        PhoneSignInResult verified;
        if (invitation is null)
        {
            verified = await phoneSignIn.VerifyAsync(request.Phone, request.Code, isSignUpAllowed, cancellationToken);
        }
        else
        {
            (verified, var redemption) = await services.GetRequiredService<InvitationService>().RedeemWithAsync(
                invitation,
                token => phoneSignIn.VerifyAsync(request.Phone, request.Code, isSignUpAllowed, token),
                result => result.Status == PhoneSignInStatus.Verified ? result.User : null,
                cancellationToken);
            if (redemption is { IsSuccess: false })
                throw InvitationErrors.Of(redemption).ToException();
        }

        switch (verified.Status)
        {
            case PhoneSignInStatus.InvalidCode:
                await RecordRefusalAsync(services, verified.PhoneNumber, SignInReasonConstants.WrongCode, cancellationToken);
                throw CodedError(AppErrorType.Unauthorized, "The code is invalid or has expired.", IdentityErrorCodeConstants.InvalidPhoneCode).ToException();
            case PhoneSignInStatus.SignUpClosed:
                await RecordRefusalAsync(services, verified.PhoneNumber, SignInReasonConstants.UnknownAccount, cancellationToken);
                throw CodedError(AppErrorType.Forbidden, "Sign-up is by invitation only; ask an admin for a link.", IdentityErrorCodeConstants.SignUpClosed).ToException();
            case PhoneSignInStatus.LockedOut:
                await RecordRefusalAsync(services, verified.PhoneNumber, SignInReasonConstants.LockedOut, cancellationToken, SignInResultConstants.LockedOut, verified.User?.Id);
                throw LockedOut().ToException();
            case not PhoneSignInStatus.Verified:
                throw NumberError(verified.Status).ToException();
        }

        var signIn = services.GetRequiredService<SignInService>();
        var result = await signIn.SignInAsync(verified.User!, [AuthenticationMethodConstants.OneTimePassword], SignInMethodConstants.PhoneCode, cancellationToken);
        if (result is { Status: SignInStatus.RequiresTwoFactor, TwoFactorTicket: { } ticket })
            throw TwoFactorChallenge(result.UserId, ticket, result.TwoFactorMethod ?? TwoFactorMethodNameConstants.Authenticator).ToException();
        if (!result.IsSuccess)
            throw FailureOf(result).ToException();

        return await SessionAsync(result, verified.User!, useCookies, request.IsPersistent, http, cancellationToken);
    }

    /// <summary>The session of a successful sign-in, as <c>sign-in</c> makes it: a cookie carrying a new session's <c>sid</c>, or a bearer token pair.</summary>
    private static async Task<IResult> SessionAsync(SignInResult result, IdentityUser user, bool? useCookies, bool isPersistent, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var principal = result.Principal!;
        if (useCookies == true)
        {
            var properties = new AuthenticationProperties();
            if (services.GetService<UserSessionService>() is { } sessions && principal.Identity is ClaimsIdentity identity)
            {
                var methods = principal.FindAll(AuthenticationMethodConstants.ClaimType).Select(claim => claim.Value);
                var session = await sessions.StartAsync(user, methods, result.SignInLogEntryId, isPersistent, cancellationToken);
                identity.AddClaim(new Claim(services.GetRequiredService<IdentityCoreOptions>().Claims.SessionIdClaimType, session.Id.ToString()));
                properties.IssuedUtc = session.CreatedAt;
                properties.ExpiresUtc = session.ExpiresAt;
                properties.IsPersistent = sessions.IsCookiePersistent(isPersistent);
            }

            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
            return Results.NoContent();
        }

        var issuer = services.GetRequiredService<ITokenIssuer>();
        var lifetime = services.GetService<JwtTokenIssuerOptions>()?.Lifetime ?? TimeSpan.FromHours(1);
        var refreshToken = services.GetService<RefreshTokenService>() is { } refresh ? (await refresh.IssueAsync(user, isPersistent, cancellationToken)).Token : null;
        return Results.Ok(ApiResponse<AccessTokenDto>.Ok(new AccessTokenDto
        {
            AccessToken = issuer.Issue(principal.Claims, new TokenIssuanceContext { Lifetime = lifetime }),
            ExpiresIn = (int)lifetime.TotalSeconds,
            RefreshToken = refreshToken,
        }));
    }

    private static UserPhoneSignInService ServiceOf(HttpContext http)
    {
        var phoneSignIn = http.RequestServices.GetRequiredService<UserPhoneSignInService>();
        return phoneSignIn.IsEnabled
            ? phoneSignIn
            : throw CodedError(AppErrorType.NotFound, "Phone sign-in is off on this instance.", PhoneSignInErrorCodeConstants.PhoneSignInUnavailable).ToException();
    }

    private static async Task RecordRefusalAsync(
        IServiceProvider services, string? phone, string reason, CancellationToken cancellationToken, string result = SignInResultConstants.Failed, Guid? userId = null)
    {
        if (services.GetService<SignInLogService>() is { } log)
            await log.RecordAsync(new SignInAttempt { Method = SignInMethodConstants.PhoneCode, Result = result, Reason = reason, Subject = phone, UserId = userId }, cancellationToken);
    }

    private static AppError LockedOut()
        => CodedError(AppErrorType.TooManyRequests, "The account is temporarily locked out.", IdentityErrorCodeConstants.LockedOut);

    private static AppError NumberError(PhoneSignInStatus status) => status == PhoneSignInStatus.CountryNotAllowed
        ? CodedError(AppErrorType.Validation, "Phone numbers from this country cannot sign in here.", PhoneSignInErrorCodeConstants.PhoneCountryNotAllowed)
        : CodedError(AppErrorType.Validation, "Enter the phone number in international format, such as +998901234567.", PhoneSignInErrorCodeConstants.InvalidPhoneNumber);

    /// <summary>The 401 a second factor answers with; the code cannot be repeated, so the ticket rides along for <c>sign-in/two-factor</c>.</summary>
    private static AppError TwoFactorChallenge(string? userId, string ticket, string method)
    {
        var metadata = MessageKey(IdentityErrorCodeConstants.TwoFactorRequired);
        metadata[AppErrorProblemDetailsFactory.ExtensionsMetadataKey] = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["identityCode"] = IdentityErrorCodeConstants.TwoFactorRequired,
            ["twoFactorMethod"] = method,
            ["userId"] = userId,
            ["twoFactorTicket"] = ticket,
        };
        return AppError.Of(AppErrorType.Unauthorized, "Enter your two-factor code.", metadata);
    }

    private static AppError FailureOf(SignInResult result) => result.Status switch
    {
        SignInStatus.LockedOut => LockedOut(),
        SignInStatus.NotAllowed => UserAccountEndpointRouteBuilderExtensions.NotAllowedError(result.UnmetPrecondition),
        _ => CodedError(AppErrorType.Unauthorized, "The sign-in could not be completed.", IdentityErrorCodeConstants.SignInFailed),
    };

    /// <summary>An error whose code a client reads from the problem's <c>identityCode</c> extension, beside the message key translation uses.</summary>
    private static AppError CodedError(AppErrorType type, string message, string code)
    {
        var metadata = MessageKey(code);
        metadata[AppErrorProblemDetailsFactory.ExtensionsMetadataKey] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["identityCode"] = code };
        return AppError.Of(type, message, metadata);
    }

    private static Dictionary<string, object?> MessageKey(string code)
        => new(StringComparer.Ordinal) { ["messageKey"] = code, ["identityCode"] = code };
}

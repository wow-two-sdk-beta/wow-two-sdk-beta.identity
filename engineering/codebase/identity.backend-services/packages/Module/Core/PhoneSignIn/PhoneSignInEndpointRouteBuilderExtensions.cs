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
using WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;
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
    ///   - with trusted contacts registered (<c>AddTrustedContacts</c>): <c>phone-sign-in/targets</c> takes <c>{ phone }</c> and
    ///     answers <c>{ trustedContacts: [{ id, label }] }</c>, the masked contacts of the account holding the number, empty
    ///     when none; <c>send</c> takes an optional <c>targetId</c>, one of those ids, and sends the code to that contact's
    ///     Telegram (<c>404</c> <c>TrustedContactNotFound</c>, <c>409</c> <c>TrustedContactUnavailable</c>); <c>phone-sign-in</c>
    ///     takes such a code as well, signing in through the contact (sign-in log method <c>trusted_contact_code</c>, the
    ///     session naming the contact, a notice to the account's own chat), and when the number's person is someone's trusted
    ///     contact answers <c>200</c> <c>{ accountChoice: { token, accounts } }</c> with no session; <c>phone-sign-in/choose</c>
    ///     takes <c>{ token, userId, isPersistent }</c> and signs in as the account picked, as <c>phone-sign-in</c> does
    ///     (<c>401</c> <c>InvalidAccountChoice</c>)
    ///   - a new device (<see cref="DeviceConfirmationService"/>): the account's own code on a browser it hasn't used answers
    ///     <c>200</c> <c>{ deviceConfirmation: { token, expiresAt, contacts } }</c> with no session, or <c>409</c>
    ///     <c>DeviceConfirmationUnavailable</c> when no contact's chat took the notice; <c>phone-sign-in/confirmation</c> takes
    ///     <c>{ token, isPersistent }</c> and answers <c>200</c> <c>{ status: "pending" }</c>, then signs in as <c>phone-sign-in</c>
    ///     does once a contact approved; <c>403</c> <c>DeviceConfirmationDenied</c>, <c>401</c> <c>DeviceConfirmationExpired</c>
    ///   - every route but the polled <c>phone-sign-in/confirmation</c>, whose token is 32 random bytes, takes the per-address
    ///     <see cref="AccountRateLimitPolicyConstants.Credentials"/> policy
    /// </remarks>
    /// <param name="endpoints">The account API's route group.</param>
    public static IEndpointRouteBuilder MapPhoneSignInEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost("/phone-sign-in/send", SendAsync).LimitCredentials(endpoints);
        endpoints.MapPost("/phone-sign-in", SignInAsync).LimitCredentials(endpoints);
        if (endpoints.ServiceProvider.GetService<IServiceProviderIsService>()?.IsService(typeof(TrustedContactService)) == true)
        {
            endpoints.MapPost("/phone-sign-in/targets", TargetsAsync).LimitCredentials(endpoints);
            endpoints.MapPost("/phone-sign-in/choose", ChooseAsync).LimitCredentials(endpoints);
            endpoints.MapPost("/phone-sign-in/confirmation", ConfirmationAsync);
        }

        return endpoints;
    }

    private static async Task<IResult> TargetsAsync(PhoneSignInTargetsApiRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        _ = ServiceOf(http);
        var targets = await http.RequestServices.GetRequiredService<TrustedContactService>().ListTargetsAsync(request.Phone, cancellationToken);
        return Results.Ok(ApiResponse<PhoneSignInTargetsDto>.Ok(new PhoneSignInTargetsDto
        {
            TrustedContacts = [.. targets.Select(target => new PhoneSignInTargetDto { Id = target.Id, Label = target.Label })],
        }));
    }

    private static async Task<IResult> ChooseAsync(PhoneSignInChooseApiRequest request, bool? useCookies, string? clientId, HttpContext http, CancellationToken cancellationToken)
    {
        _ = ServiceOf(http);
        var services = http.RequestServices;
        if (useCookies != true && services.GetService<ITokenIssuer>() is null)
            throw CodedError(AppErrorType.Validation, "Bearer sign-in is not enabled on this instance; sign in with ?useCookies=true.", AccountApiErrorCodeConstants.BearerSignInUnsupported).ToException();
        if (!string.IsNullOrWhiteSpace(clientId) && services.GetService<SignInLogService>() is { } log)
            log.ClientId = clientId;

        var chosen = await services.GetRequiredService<TrustedContactService>().ChooseAsync(request.Token, request.UserId, cancellationToken)
            ?? throw CodedError(AppErrorType.Unauthorized, "The account choice has expired or does not offer that account; sign in again.", TrustedContactErrorCodeConstants.InvalidAccountChoice).ToException();
        return await SignInPersonAsync(chosen.User, chosen.TrustedContactUserId, useCookies, request.IsPersistent, http, cancellationToken);
    }

    private static async Task<IResult> ConfirmationAsync(PhoneSignInConfirmationApiRequest request, bool? useCookies, string? clientId, HttpContext http, CancellationToken cancellationToken)
    {
        _ = ServiceOf(http);
        var services = http.RequestServices;
        if (useCookies != true && services.GetService<ITokenIssuer>() is null)
            throw CodedError(AppErrorType.Validation, "Bearer sign-in is not enabled on this instance; sign in with ?useCookies=true.", AccountApiErrorCodeConstants.BearerSignInUnsupported).ToException();
        if (!string.IsNullOrWhiteSpace(clientId) && services.GetService<SignInLogService>() is { } log)
            log.ClientId = clientId;

        var poll = await services.GetRequiredService<DeviceConfirmationService>().PollAsync(request.Token, useCookies == true, cancellationToken);
        switch (poll.State)
        {
            case DeviceConfirmationState.Pending:
                return Results.Ok(ApiResponse<DeviceConfirmationPendingDto>.Ok(new DeviceConfirmationPendingDto()));
            case DeviceConfirmationState.Approved:
                return await SignInPersonAsync(poll.User!, trustedContactUserId: null, useCookies, request.IsPersistent, http, cancellationToken, isDeviceConfirmed: true);
            case DeviceConfirmationState.Denied:
                await RecordRefusalAsync(services, poll.User?.PhoneNumber, SignInReasonConstants.DeviceDenied, cancellationToken, userId: poll.User?.Id);
                throw CodedError(AppErrorType.Forbidden, "Your trusted contact did not confirm this device.", TrustedContactErrorCodeConstants.DeviceConfirmationDenied).ToException();
            default:
                throw CodedError(AppErrorType.Unauthorized, "The confirmation has expired; sign in again.", TrustedContactErrorCodeConstants.DeviceConfirmationExpired).ToException();
        }
    }

    private static async Task<IResult> SendAsync(PhoneSignInSendApiRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var phoneSignIn = ServiceOf(http);
        if (!string.IsNullOrWhiteSpace(request.TargetId))
            return await SendToContactAsync(request, http, cancellationToken);

        var result = await phoneSignIn.SendAsync(request.Phone, http.Connection.RemoteIpAddress?.ToString(), CultureInfo.CurrentUICulture, cancellationToken);
        return result.Status switch
        {
            PhoneSignInStatus.CodeSent => Results.Ok(ApiResponse<PhoneSignInSentDto>.Ok(new PhoneSignInSentDto { IsCodeSent = true })),
            PhoneSignInStatus.LinkRequired => Results.Ok(ApiResponse<PhoneSignInSentDto>.Ok(new PhoneSignInSentDto { IsCodeSent = false, TelegramLinkUrl = result.TelegramLinkUrl })),
            PhoneSignInStatus.RateLimited => throw CodedError(AppErrorType.TooManyRequests, "A code was sent moments ago; wait before asking again.", PhoneSignInErrorCodeConstants.PhoneCodeRateLimited).ToException(),
            _ => throw NumberError(result.Status).ToException(),
        };
    }

    private static async Task<IResult> SendToContactAsync(PhoneSignInSendApiRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var trusted = http.RequestServices.GetService<TrustedContactService>()
            ?? throw CodedError(AppErrorType.NotFound, "Trusted contacts are off on this instance.", TrustedContactErrorCodeConstants.TrustedContactsOff).ToException();
        var status = await trusted.SendSignInCodeAsync(request.Phone, request.TargetId, http.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return status switch
        {
            TrustedContactSendStatus.CodeSent => Results.Ok(ApiResponse<PhoneSignInSentDto>.Ok(new PhoneSignInSentDto { IsCodeSent = true })),
            TrustedContactSendStatus.RateLimited => throw CodedError(AppErrorType.TooManyRequests, "A code was sent moments ago; wait before asking again.", PhoneSignInErrorCodeConstants.PhoneCodeRateLimited).ToException(),
            TrustedContactSendStatus.NotFound => throw CodedError(AppErrorType.NotFound, "No such trusted contact for this number.", TrustedContactErrorCodeConstants.TrustedContactNotFound).ToException(),
            TrustedContactSendStatus.Unavailable => throw CodedError(AppErrorType.Conflict, "The trusted contact cannot receive a code now; pick another or use your own phone.", TrustedContactErrorCodeConstants.TrustedContactUnavailable).ToException(),
            TrustedContactSendStatus.CountryNotAllowed => throw NumberError(PhoneSignInStatus.CountryNotAllowed).ToException(),
            _ => throw NumberError(PhoneSignInStatus.InvalidNumber).ToException(),
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

        // A trusted contact signing in with their own number picks the account: their own, or one trusting them.
        if (verified is { TrustedContactUserId: null, IsUserCreated: false, User: { } person }
            && invitation is null
            && services.GetService<TrustedContactService>() is { } trusted
            && await trusted.OfferAccountsAsync(person, cancellationToken) is { } choice)
        {
            return Results.Ok(ApiResponse<PhoneSignInAccountChoiceDto>.Ok(new PhoneSignInAccountChoiceDto
            {
                AccountChoice = new AccountChoiceDto
                {
                    Token = choice.Token,
                    Accounts =
                    [
                        .. choice.Accounts.Select(account => new AccountChoiceAccountDto
                        {
                            UserId = account.UserId,
                            DisplayName = account.DisplayName,
                            PictureUrl = account.PictureUrl,
                            IsSelf = account.IsSelf,
                        }),
                    ],
                },
            }));
        }

        return await SignInPersonAsync(verified.User!, verified.TrustedContactUserId, useCookies, request.IsPersistent, http, cancellationToken);
    }

    /// <summary>
    /// Signs <paramref name="user"/> in after a verified phone code: their own (<c>phone_code</c>), or one a trusted contact
    /// received (<c>trusted_contact_code</c>, the session naming the contact, a notice to the account's own chat). The person's
    /// own code on a new device waits for a contact's press first, unless <paramref name="isDeviceConfirmed"/>.
    /// </summary>
    private static async Task<IResult> SignInPersonAsync(
        IdentityUser user, Guid? trustedContactUserId, bool? useCookies, bool isPersistent, HttpContext http, CancellationToken cancellationToken, bool isDeviceConfirmed = false)
    {
        var services = http.RequestServices;
        var devices = services.GetService<DeviceConfirmationService>();
        if (devices is { IsEnabled: true } && trustedContactUserId is null && !isDeviceConfirmed)
        {
            var (ipMasked, userAgent) = PersonalDataMasks.Describe(http);
            var check = await devices.CheckAsync(user, DeviceCookie.Ensure(http, useCookies == true), ipMasked, userAgent, cancellationToken);
            switch (check.Status)
            {
                case DeviceCheckStatus.Waiting:
                    return Results.Ok(ApiResponse<PhoneSignInDeviceConfirmationDto>.Ok(new PhoneSignInDeviceConfirmationDto
                    {
                        DeviceConfirmation = new DeviceConfirmationDto { Token = check.Token!, ExpiresAt = check.ExpiresAt!.Value, Contacts = check.Contacts },
                    }));
                case DeviceCheckStatus.Unavailable:
                    throw CodedError(AppErrorType.Conflict, "Your trusted contacts cannot be reached now; try again later or sign in from a device you have used.", TrustedContactErrorCodeConstants.DeviceConfirmationUnavailable).ToException();
            }
        }

        if (trustedContactUserId is not null && services.GetService<SignInLogService>() is { } log)
            log.TrustedContactUserId = trustedContactUserId;

        var method = trustedContactUserId is null ? SignInMethodConstants.PhoneCode : SignInMethodConstants.TrustedContactCode;
        var result = await services.GetRequiredService<SignInService>().SignInAsync(user, [AuthenticationMethodConstants.OneTimePassword], method, cancellationToken);
        if (result is { Status: SignInStatus.RequiresTwoFactor, TwoFactorTicket: { } ticket })
            throw TwoFactorChallenge(result.UserId, ticket, result.TwoFactorMethod ?? TwoFactorMethodNameConstants.Authenticator).ToException();
        if (!result.IsSuccess)
            throw FailureOf(result).ToException();

        if (trustedContactUserId is { } contactId && services.GetService<TrustedContactService>() is { } trusted)
        {
            await trusted.NotifySignInAsync(user, contactId, cancellationToken);
            if (devices is { IsEnabled: true } && useCookies == true)
                await devices.RememberThroughContactAsync(user.Id, contactId, DeviceCookie.Ensure(http, isCookieClient: true), cancellationToken);
        }

        return await SessionAsync(result, user, useCookies, isPersistent, trustedContactUserId, http, cancellationToken);
    }

    /// <summary>The session of a successful sign-in, as <c>sign-in</c> makes it: a cookie carrying a new session's <c>sid</c>, or a bearer token pair.</summary>
    private static async Task<IResult> SessionAsync(SignInResult result, IdentityUser user, bool? useCookies, bool isPersistent, Guid? trustedContactUserId, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var principal = result.Principal!;
        if (useCookies == true)
        {
            var properties = new AuthenticationProperties();
            if (services.GetService<UserSessionService>() is { } sessions && principal.Identity is ClaimsIdentity identity)
            {
                var methods = principal.FindAll(AuthenticationMethodConstants.ClaimType).Select(claim => claim.Value);
                var session = await sessions.StartAsync(user, methods, result.SignInLogEntryId, isPersistent, trustedContactUserId, cancellationToken);
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

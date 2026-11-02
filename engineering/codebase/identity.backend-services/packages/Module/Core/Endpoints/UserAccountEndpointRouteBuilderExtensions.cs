using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Emails;
using WoW.Two.Sdk.Beta.Be.Identity.Core.EmailSignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passkeys;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;
using WoW.Two.Sdk.Beta.Be.Identity.Core.RefreshTokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;
using WoW.Two.Sdk.Beta.Be.Identity.Core.TwoFactor;
using WoW.Two.Sdk.Beta.Be.Identity.Jwt.Issuance;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Factories;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>Maps the account HTTP API over the registered identity slices.</summary>
public static partial class UserAccountEndpointRouteBuilderExtensions
{
    private static readonly JsonSerializerOptions ExportJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// Maps <c>sign-up</c>, <c>sign-in</c>, <c>refresh</c>, <c>sign-out</c>, <c>confirm-email</c>,
    /// <c>resend-confirmation-email</c>, <c>forgot-password</c>, <c>reset-password</c>, <c>manage/info</c> and, with the
    /// two-factor slice registered, <c>manage/2fa</c> (status, authenticator, enable, disable, recovery-codes, preferred,
    /// methods/{method}/send, methods/{method}/enable, plus <c>sign-in/two-factor</c>); with email sign-in registered,
    /// <c>email-sign-in/send</c> and <c>email-sign-in</c>; with personal data registered, <c>manage/personal-data</c> and
    /// <c>manage/delete-account</c>; with the passkey slice registered, <c>passkeys/sign-in/options</c>,
    /// <c>passkeys/sign-in</c> and <c>manage/passkeys</c> (list, options, add, remove); and, with the sessions slice
    /// registered, <c>manage/sessions</c> (list, revoke one, revoke all) under <paramref name="endpoints"/> — mount it on a
    /// group such as <c>app.MapGroup("/api/account")</c>. Sign-in returns a bearer session (JWT via <see cref="ITokenIssuer"/>,
    /// plus a refresh token when registered) or, with <c>?useCookies=true</c>, a cookie that carries a new session's
    /// <c>sid</c> when sessions are registered. Each sign-in route takes an optional <c>?clientId=</c> the sign-in log
    /// records. <c>sign-up</c> answers <c>403</c> (message key <c>SignUpClosed</c>) unless <see cref="SignUpOptions.IsEnabled"/>
    /// is on. A sign-in whose password matched but breaks the current password rules answers <c>200</c> with
    /// <c>isPasswordChangeRequired</c> and a ticket instead of a session. Bearer sign-in and <c>refresh</c> answer <c>400</c>
    /// (message key <c>BearerSignInUnsupported</c>) on a host without JWT issuance or refresh tokens, such as a cookie-only
    /// instance. With the password change slice registered, <c>manage/password/confirmation</c>, <c>manage/password</c> and
    /// <c>password/change</c> map too. Failures surface as problem details, translated when error translation is enabled.
    /// </summary>
    /// <param name="endpoints">The route builder or group.</param>
    public static IEndpointRouteBuilder MapUserAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/sign-up", SignUpAsync);
        endpoints.MapPost("/sign-in", SignInAsync);
        endpoints.MapPost("/refresh", RefreshAsync);
        endpoints.MapPost("/sign-out", SignOutAsync);
        endpoints.MapGet("/confirm-email", ConfirmEmailAsync);
        endpoints.MapPost("/resend-confirmation-email", ResendConfirmationAsync);
        endpoints.MapPost("/forgot-password", ForgotPasswordAsync);
        endpoints.MapPost("/reset-password", ResetPasswordAsync);
        endpoints.MapGet("/manage/info", InfoAsync).RequireAuthorization();

        if (endpoints.ServiceProvider.GetService<IServiceProviderIsService>()?.IsService(typeof(UserPasswordChangeService)) == true)
            MapPasswordChangeEndpoints(endpoints);

        if (endpoints.ServiceProvider.GetService<IServiceProviderIsService>()?.IsService(typeof(UserTwoFactorService)) == true)
        {
            endpoints.MapGet("/manage/2fa", TwoFactorStatusAsync).RequireAuthorization();
            endpoints.MapPost("/manage/2fa/authenticator", ResetAuthenticatorAsync).RequireAuthorization();
            endpoints.MapPost("/manage/2fa/enable", EnableTwoFactorAsync).RequireAuthorization();
            endpoints.MapPost("/manage/2fa/disable", DisableTwoFactorAsync).RequireAuthorization();
            endpoints.MapPost("/manage/2fa/recovery-codes", RegenerateRecoveryCodesAsync).RequireAuthorization();
            endpoints.MapPost("/manage/2fa/preferred", SetPreferredMethodAsync).RequireAuthorization();
            endpoints.MapPost("/manage/2fa/methods/{method}/send", SendMethodCodeAsync).RequireAuthorization();
            endpoints.MapPost("/manage/2fa/methods/{method}/enable", EnableMethodAsync).RequireAuthorization();
            endpoints.MapPost("/sign-in/two-factor", SecondFactorSignInAsync);
        }

        if (endpoints.ServiceProvider.GetService<IServiceProviderIsService>()?.IsService(typeof(UserPersonalDataService)) == true)
        {
            endpoints.MapGet("/manage/personal-data", ExportPersonalDataAsync).RequireAuthorization();
            endpoints.MapPost("/manage/delete-account", DeleteAccountAsync).RequireAuthorization();
        }

        if (endpoints.ServiceProvider.GetService<IServiceProviderIsService>()?.IsService(typeof(UserEmailSignInService)) == true)
        {
            endpoints.MapPost("/email-sign-in/send", SendEmailSignInAsync);
            endpoints.MapPost("/email-sign-in", EmailSignInAsync);
        }

        if (endpoints.ServiceProvider.GetService<IServiceProviderIsService>()?.IsService(typeof(UserPasskeyService)) == true)
        {
            endpoints.MapPost("/passkeys/sign-in/options", PasskeySignInOptions);
            endpoints.MapPost("/passkeys/sign-in", PasskeySignInAsync);
            endpoints.MapGet("/manage/passkeys", ListPasskeysAsync).RequireAuthorization();
            endpoints.MapPost("/manage/passkeys/options", PasskeyCreationOptionsAsync).RequireAuthorization();
            endpoints.MapPost("/manage/passkeys", AddPasskeyAsync).RequireAuthorization();
            endpoints.MapDelete("/manage/passkeys/{id:guid}", RemovePasskeyAsync).RequireAuthorization();
        }

        if (endpoints.ServiceProvider.GetService<IServiceProviderIsService>()?.IsService(typeof(UserSessionService)) == true)
        {
            endpoints.MapGet("/manage/sessions", ListSessionsAsync).RequireAuthorization();
            endpoints.MapDelete("/manage/sessions/{id:guid}", RevokeSessionAsync).RequireAuthorization();
            endpoints.MapDelete("/manage/sessions", RevokeAllSessionsAsync).RequireAuthorization();
        }

        return endpoints;
    }

    private static async Task<IResult> SignUpAsync(SignUpApiRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        if (services.GetService<IOptionsMonitor<SignUpOptions>>()?.CurrentValue.IsEnabled != true)
            throw AppError.Of(AppErrorType.Forbidden, "Sign-up is closed; ask an admin to add you.", MessageKey(IdentityErrorCodeConstants.SignUpClosed)).ToException();

        var user = new IdentityUser { UserName = string.IsNullOrWhiteSpace(request.UserName) ? request.Email : request.UserName, Email = request.Email };
        var created = await services.GetRequiredService<UserPasswordService>().CreateWithPasswordAsync(user, request.Password, cancellationToken);
        if (!created.IsSuccess)
            throw created.ToValidationError().ToException();

        var isConfirmationSent = await SendConfirmationAsync(services, user, cancellationToken);
        return Results.Ok(ApiResponse<SignedUpAccountDto>.Ok(new SignedUpAccountDto { UserId = IdOf(user), IsConfirmationSent = isConfirmationSent }));
    }

    private static async Task<IResult> SignInAsync(SignInApiRequest request, bool? useCookies, string? clientId, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        NameClient(services, clientId);
        RequireSessionKind(services, useCookies);
        var signIn = services.GetRequiredService<SignInService>();
        var firstFactor = await signIn.PasswordSignInAsync(request.Identifier, request.Password, cancellationToken);
        var result = firstFactor;
        if (firstFactor is { Status: SignInStatus.RequiresTwoFactor, TwoFactorTicket: { } ticket } && TryParseKey(firstFactor.UserId, out var userId))
        {
            var method = request.TwoFactorMethod ?? firstFactor.TwoFactorMethod ?? TwoFactorMethodNameConstants.Authenticator;
            if (!string.IsNullOrWhiteSpace(request.TwoFactorCode))
                result = await signIn.TwoFactorSignInAsync(userId, ticket, method, request.TwoFactorCode, cancellationToken);
            else if (!string.IsNullOrWhiteSpace(request.RecoveryCode))
                result = await signIn.RecoveryCodeSignInAsync(userId, ticket, request.RecoveryCode, cancellationToken);
            else
                throw (await TwoFactorChallengeAsync(services, signIn, userId, ticket, method, request.TwoFactorMethod is not null, cancellationToken)).ToException();

            result = await signIn.FinishPasswordSignInAsync(firstFactor, result, cancellationToken);
        }

        return result.Status == SignInStatus.PasswordChangeRequired
            ? PasswordChangeRequired(result, http)
            : await SessionAsync(result, useCookies, request.IsPersistent, http, cancellationToken);
    }

    private static IResult PasskeySignInOptions(HttpContext http)
        => Results.Ok(ApiResponse<PasskeyCeremonyDto>.Ok(CeremonyOf(http.RequestServices.GetRequiredService<UserPasskeyService>().BeginSignIn())));

    private static async Task<IResult> PasskeySignInAsync(PasskeySignInApiRequest request, bool? useCookies, string? clientId, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        NameClient(services, clientId);
        RequireSessionKind(services, useCookies);
        var result = await services.GetRequiredService<SignInService>().PasskeySignInAsync(request.State, request.Credential.GetRawText(), cancellationToken);
        if (result.Status == SignInStatus.Failed)
            throw Unauthorized(IdentityErrorCodeConstants.InvalidPasskey, "The passkey could not be verified; try again.").ToException();

        return await SessionAsync(result, useCookies, request.IsPersistent, http, cancellationToken);
    }

    private static async Task<IResult> SendEmailSignInAsync(EmailSignInSendApiRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var signIn = services.GetRequiredService<UserEmailSignInService>();
        var mail = services.GetService<UserAccountMailService>();
        if (string.Equals(request.Method, "code", StringComparison.OrdinalIgnoreCase))
        {
            if (await signIn.CreateCodeAsync(request.Email, cancellationToken) is { } code && mail is not null)
                await mail.SendSignInCodeAsync(code.User, code.Code.Code!, cancellationToken);
        }
        else if (await signIn.CreateLinkTokenAsync(request.Email, cancellationToken) is { } link && mail is not null)
        {
            await mail.SendMagicLinkAsync(link.User, link.Token.Code!, cancellationToken);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> EmailSignInAsync(EmailSignInApiRequest request, bool? useCookies, string? clientId, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        NameClient(services, clientId);
        RequireSessionKind(services, useCookies);
        var emailSignIn = services.GetRequiredService<UserEmailSignInService>();
        var byLink = !string.IsNullOrWhiteSpace(request.Token);
        var logMethod = byLink ? SignInMethodConstants.EmailLink : SignInMethodConstants.EmailCode;
        var user = byLink
            ? await emailSignIn.VerifyLinkTokenAsync(request.Email, request.Token!, cancellationToken)
            : await emailSignIn.VerifyCodeAsync(request.Email, request.Code ?? string.Empty, cancellationToken);
        if (user is null)
        {
            await RecordRefusalAsync(services, logMethod, request.Email, SignInReasonConstants.InvalidLinkOrCode, cancellationToken);
            throw Unauthorized(IdentityErrorCodeConstants.InvalidEmailSignIn, "The link or code is invalid or has expired.").ToException();
        }

        var signIn = services.GetRequiredService<SignInService>();
        var result = await signIn.SignInAsync(user, [AuthenticationMethodConstants.OneTimePassword], logMethod, cancellationToken);
        if (result is { Status: SignInStatus.RequiresTwoFactor, TwoFactorTicket: { } ticket } && TryParseKey(result.UserId, out var userId))
        {
            var method = result.TwoFactorMethod ?? TwoFactorMethodNameConstants.Authenticator;
            throw (await TwoFactorChallengeAsync(services, signIn, userId, ticket, method, chosen: false, cancellationToken, exposeTicket: true)).ToException();
        }

        return await SessionAsync(result, useCookies, request.IsPersistent, http, cancellationToken);
    }

    private static async Task<IResult> SecondFactorSignInAsync(TwoFactorSignInApiRequest request, bool? useCookies, string? clientId, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        NameClient(services, clientId);
        RequireSessionKind(services, useCookies);
        var method = request.TwoFactorMethod ?? TwoFactorMethodNameConstants.Authenticator;
        if (!TryParseKey(request.UserId, out var userId))
        {
            await RecordRefusalAsync(services, SignInMethodConstants.SecondFactor(method), null, SignInReasonConstants.InvalidTicket, cancellationToken);
            throw Unauthorized(IdentityErrorCodeConstants.SignInFailed, "The sign-in has expired; start again.").ToException();
        }

        var signIn = services.GetRequiredService<SignInService>();
        SignInResult result;
        if (!string.IsNullOrWhiteSpace(request.TwoFactorCode))
            result = await signIn.TwoFactorSignInAsync(userId, request.Ticket, method, request.TwoFactorCode, cancellationToken);
        else if (!string.IsNullOrWhiteSpace(request.RecoveryCode))
            result = await signIn.RecoveryCodeSignInAsync(userId, request.Ticket, request.RecoveryCode, cancellationToken);
        else
            throw (await TwoFactorChallengeAsync(services, signIn, userId, request.Ticket, method, request.TwoFactorMethod is not null, cancellationToken, exposeTicket: true)).ToException();

        return await SessionAsync(result, useCookies, request.IsPersistent, http, cancellationToken);
    }

    /// <summary>
    /// The session of a successful sign-in: a cookie with <c>useCookies</c>, carrying a new session's <c>sid</c> and
    /// expiring with it when sessions are registered; else a bearer token pair. <paramref name="isPersistent"/> asks for
    /// the extended lifetime and a cookie that outlives the browser, where the session settings offer it.
    /// </summary>
    private static async Task<IResult> SessionAsync(SignInResult result, bool? useCookies, bool isPersistent, HttpContext http, CancellationToken cancellationToken)
    {
        if (!result.IsSuccess)
            throw FailureOf(result.Status).ToException();

        var services = http.RequestServices;
        if (useCookies == true)
        {
            var principal = result.Principal!;
            var properties = new AuthenticationProperties();
            if (services.GetService<UserSessionService>() is { } sessions
                && principal.Identity is ClaimsIdentity identity
                && await FindAsync(services, result.UserId, cancellationToken) is { } signedIn)
            {
                var methods = principal.FindAll(AuthenticationMethodConstants.ClaimType).Select(claim => claim.Value);
                var session = await sessions.StartAsync(signedIn, methods, result.SignInLogEntryId, isPersistent, cancellationToken);
                identity.AddClaim(new Claim(services.GetRequiredService<IdentityCoreOptions>().Claims.SessionIdClaimType, session.Id.ToString()));
                properties.IssuedUtc = session.CreatedAt;
                properties.ExpiresUtc = session.ExpiresAt;
                properties.IsPersistent = sessions.IsCookiePersistent(isPersistent);
            }

            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
            return Results.NoContent();
        }

        var user = await FindAsync(services, result.UserId, cancellationToken);
        return Results.Ok(ApiResponse<AccessTokenDto>.Ok(await IssueAsync(services, result.Principal!, user, null, isPersistent, cancellationToken)));
    }

    private static async Task<IResult> RefreshAsync(RefreshAccessApiRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var refresh = services.GetService<RefreshTokenService>() is { } registered && services.GetService<ITokenIssuer>() is not null
            ? registered
            : throw BearerUnsupported("Refreshing is not enabled on this instance; sign in with ?useCookies=true.").ToException();
        var redeemed = await refresh.RedeemAsync(request.RefreshToken, cancellationToken);
        if (!redeemed.IsSuccess)
            throw Unauthorized(IdentityErrorCodeConstants.InvalidRefreshToken, "The session has expired; sign in again.").ToException();

        var renewed = await services.GetRequiredService<SignInService>().RenewAsync(redeemed.User!, cancellationToken);
        if (!renewed.IsSuccess)
        {
            await refresh.RevokeAsync(redeemed.Token!.Token, cancellationToken);
            throw FailureOf(renewed.Status).ToException();
        }

        return Results.Ok(ApiResponse<AccessTokenDto>.Ok(await IssueAsync(services, renewed.Principal!, redeemed.User, redeemed.Token!.Token, isPersistent: false, cancellationToken)));
    }

    /// <summary>
    /// Ends the caller's session. The body is optional, so a browser signs out with an empty <c>POST</c>; a bearer client
    /// sends <c>{ refreshToken }</c> as JSON to revoke it too.
    /// </summary>
    private static async Task<IResult> SignOutAsync(ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var request = await ReadOptionalBodyAsync<SignOutApiRequest>(http, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request?.RefreshToken) && http.RequestServices.GetService<RefreshTokenService>() is { } refresh)
            await refresh.RevokeAsync(request.RefreshToken, cancellationToken);

        // The cookie's session ends with it, and with it every OIDC token issued under it.
        if (http.RequestServices.GetService<UserSessionService>() is { } sessions && SessionOf(http.RequestServices, principal) is var (userId, sessionId))
            await sessions.RevokeAsync(userId, sessionId, SessionRevokeReasonConstants.SignOut, cancellationToken);

        if (await http.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync(CookieAuthenticationDefaults.AuthenticationScheme) is not null)
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return Results.NoContent();
    }

    private static async Task<IResult> ConfirmEmailAsync(string userId, string token, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var user = await FindAsync(services, userId, cancellationToken);
        var confirmed = user is null
            ? IdentityResult.Failed(new IdentityError { Code = IdentityErrorCodeConstants.InvalidToken, Description = "The link is invalid or has expired." })
            : await services.GetRequiredService<UserEmailService>().ConfirmEmailAsync(user, token, cancellationToken);
        if (!confirmed.IsSuccess)
            throw confirmed.ToValidationError().ToException();

        return Results.NoContent();
    }

    private static async Task<IResult> ResendConfirmationAsync(EmailAddressApiRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var user = await services.GetRequiredService<UserAccountService>().FindByEmailAsync(request.Email, cancellationToken);
        if (user is { IsEmailConfirmed: false })
            await SendConfirmationAsync(services, user, cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> ForgotPasswordAsync(EmailAddressApiRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var user = await services.GetRequiredService<UserAccountService>().FindByEmailAsync(request.Email, cancellationToken);
        if (user is not null && services.GetService<UserAccountMailService>() is { } mail)
        {
            var token = services.GetRequiredService<UserPasswordService>().IssuePasswordResetToken(user);
            await mail.SendPasswordResetAsync(user, token, cancellationToken);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> ResetPasswordAsync(ResetPasswordApiRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var user = await services.GetRequiredService<UserAccountService>().FindByEmailAsync(request.Email, cancellationToken);
        var reset = user is null
            ? IdentityResult.Failed(new IdentityError { Code = IdentityErrorCodeConstants.InvalidToken, Description = "The reset link is invalid or has expired." })
            : await services.GetRequiredService<UserPasswordService>().ResetPasswordAsync(user, request.ResetToken, request.NewPassword, cancellationToken);
        if (!reset.IsSuccess)
            throw reset.ToValidationError().ToException();

        return Results.NoContent();
    }

    private static async Task<IResult> InfoAsync(ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var claimType = services.GetRequiredService<IdentityCoreOptions>().Claims.UserIdClaimType;
        var user = await FindAsync(services, principal.FindFirst(claimType)?.Value, cancellationToken)
            ?? throw AppErrorFactory.Unauthorized("The account no longer exists.").ToException();

        return Results.Ok(ApiResponse<AccountInfoDto>.Ok(new AccountInfoDto
        {
            UserId = IdOf(user),
            UserName = user.UserName,
            Email = user.Email,
            IsEmailConfirmed = user.IsEmailConfirmed,
            IsTwoFactorEnabled = user.IsTwoFactorEnabled,
        }));
    }

    private static async Task<IResult> TwoFactorStatusAsync(ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, twoFactor) = await TwoFactorOfAsync(principal, http, cancellationToken);
        var methods = http.RequestServices.GetService<UserTwoFactorMethodService>();
        return Results.Ok(ApiResponse<TwoFactorStatusDto>.Ok(new TwoFactorStatusDto
        {
            IsEnabled = user.IsTwoFactorEnabled,
            HasAuthenticator = await twoFactor.GetAuthenticatorKeyAsync(user, cancellationToken) is not null,
            RecoveryCodesLeft = await twoFactor.CountRecoveryCodesAsync(user, cancellationToken),
            Methods = methods is null ? [] : await methods.GetAvailableMethodsAsync(user, cancellationToken),
            PreferredMethod = methods is null ? null : await methods.GetPreferredMethodAsync(user, cancellationToken),
        }));
    }

    private static async Task<IResult> ResetAuthenticatorAsync(ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, twoFactor) = await TwoFactorOfAsync(principal, http, cancellationToken);
        var key = await twoFactor.ResetAuthenticatorKeyAsync(user, cancellationToken);
        var uri = await twoFactor.GetAuthenticatorUriAsync(user, cancellationToken);
        return Results.Ok(ApiResponse<AuthenticatorSetupDto>.Ok(new AuthenticatorSetupDto { SharedKey = key, AuthenticatorUri = uri!.ToString() }));
    }

    private static async Task<IResult> EnableTwoFactorAsync(TwoFactorCodeApiRequest request, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, twoFactor) = await TwoFactorOfAsync(principal, http, cancellationToken);
        var enabled = await twoFactor.EnableAsync(user, request.Code, cancellationToken);
        if (!enabled.IsSuccess)
            throw enabled.ToValidationError().ToException();

        var codes = await twoFactor.GenerateRecoveryCodesAsync(user, cancellationToken);
        return Results.Ok(ApiResponse<RecoveryCodesDto>.Ok(new RecoveryCodesDto { RecoveryCodes = codes }));
    }

    private static async Task<IResult> DisableTwoFactorAsync(ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, twoFactor) = await TwoFactorOfAsync(principal, http, cancellationToken);
        await twoFactor.DisableAsync(user, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> RegenerateRecoveryCodesAsync(ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, twoFactor) = await TwoFactorOfAsync(principal, http, cancellationToken);
        if (!user.IsTwoFactorEnabled)
            throw AppError.Of(AppErrorType.Conflict, "Enable two-factor before generating recovery codes.", MessageKey(IdentityErrorCodeConstants.AuthenticatorNotConfigured)).ToException();

        var codes = await twoFactor.GenerateRecoveryCodesAsync(user, cancellationToken);
        return Results.Ok(ApiResponse<RecoveryCodesDto>.Ok(new RecoveryCodesDto { RecoveryCodes = codes }));
    }

    private static async Task<IResult> SetPreferredMethodAsync(TwoFactorMethodApiRequest request, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, _) = await TwoFactorOfAsync(principal, http, cancellationToken);
        var chosen = await http.RequestServices.GetRequiredService<UserTwoFactorMethodService>().SetPreferredMethodAsync(user, request.Method, cancellationToken);
        if (!chosen.IsSuccess)
            throw chosen.ToValidationError().ToException();

        return Results.NoContent();
    }

    private static async Task<IResult> SendMethodCodeAsync(string method, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, _) = await TwoFactorOfAsync(principal, http, cancellationToken);
        var sent = await http.RequestServices.GetRequiredService<UserTwoFactorMethodService>().SendCodeAsync(user, method, cancellationToken: cancellationToken);
        return sent.IsSent
            ? Results.Ok(ApiResponse<TwoFactorCodeSentDto>.Ok(new TwoFactorCodeSentDto { Method = sent.Method, ExpiresAt = sent.ExpiresAt }))
            : throw FailureOf(sent).ToException();
    }

    private static async Task<IResult> EnableMethodAsync(string method, TwoFactorCodeApiRequest request, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, twoFactor) = await TwoFactorOfAsync(principal, http, cancellationToken);
        var enabled = await http.RequestServices.GetRequiredService<UserTwoFactorMethodService>().EnableAsync(user, method, request.Code, cancellationToken);
        if (!enabled.IsSuccess)
            throw enabled.ToValidationError().ToException();

        var codes = await twoFactor.GenerateRecoveryCodesAsync(user, cancellationToken);
        return Results.Ok(ApiResponse<RecoveryCodesDto>.Ok(new RecoveryCodesDto { RecoveryCodes = codes }));
    }

    private static async Task<IResult> ExportPersonalDataAsync(ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(principal, http, cancellationToken);
        var export = await http.RequestServices.GetRequiredService<UserPersonalDataService>().ExportAsync(user, cancellationToken);
        var json = JsonSerializer.SerializeToUtf8Bytes(export, ExportJson);
        return Results.File(json, "application/json", "personal-data.json");
    }

    private static async Task<IResult> DeleteAccountAsync(DeleteAccountApiRequest? request, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var user = await CurrentUserAsync(principal, http, cancellationToken);
        if (user.PasswordHash is not null
            && (string.IsNullOrEmpty(request?.Password)
                || services.GetService<UserPasswordService>() is not { } passwords
                || !await passwords.CheckPasswordAsync(user, request.Password, cancellationToken)))
        {
            throw AppError.Of(AppErrorType.Forbidden, "Confirm with the account's password to delete it.", MessageKey(IdentityErrorCodeConstants.PasswordMismatch)).ToException();
        }

        await services.GetRequiredService<UserPersonalDataService>().DeleteAsync(user, cancellationToken);
        if (await services.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync(CookieAuthenticationDefaults.AuthenticationScheme) is not null)
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return Results.NoContent();
    }

    private static async Task<IdentityUser> CurrentUserAsync(ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var claimType = services.GetRequiredService<IdentityCoreOptions>().Claims.UserIdClaimType;
        return await FindAsync(services, principal.FindFirst(claimType)?.Value, cancellationToken)
            ?? throw AppErrorFactory.Unauthorized("The account no longer exists.").ToException();
    }

    private static async Task<IResult> ListSessionsAsync(ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var user = await CurrentUserAsync(principal, http, cancellationToken);
        var current = SessionOf(services, principal)?.SessionId;
        var listed = await services.GetRequiredService<UserSessionService>().ListAsync(user, cancellationToken);
        return Results.Ok(ApiResponse<IReadOnlyList<SessionDto>>.Ok([.. listed.Select(session => new SessionDto
        {
            Id = session.Id,
            CreatedAt = session.CreatedAt,
            LastSeenAt = session.LastSeenAt,
            ExpiresAt = session.ExpiresAt,
            AuthenticationMethods = session.AuthenticationMethods,
            IpMasked = session.IpMasked,
            UserAgent = session.UserAgent,
            IsCurrent = session.Id == current,
        })]));
    }

    private static async Task<IResult> RevokeSessionAsync(Guid id, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var user = await CurrentUserAsync(principal, http, cancellationToken);
        if (!await services.GetRequiredService<UserSessionService>().RevokeAsync(user.Id, id, cancellationToken: cancellationToken))
            throw AppErrorFactory.NotFound("The account has no such live session.").ToException();

        if (SessionOf(services, principal)?.SessionId == id)
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return Results.NoContent();
    }

    private static async Task<IResult> RevokeAllSessionsAsync(bool? keepCurrent, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var user = await CurrentUserAsync(principal, http, cancellationToken);
        var current = SessionOf(services, principal)?.SessionId;
        await services.GetRequiredService<UserSessionService>().RevokeAllAsync(user, keepCurrent == true ? current : null, cancellationToken);
        if (current is not null && keepCurrent != true)
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return Results.NoContent();
    }

    /// <summary>The person and session a cookie principal names; null for a principal without <c>sid</c>, such as a bearer token.</summary>
    private static (Guid UserId, Guid SessionId)? SessionOf(IServiceProvider services, ClaimsPrincipal principal)
    {
        var claims = services.GetRequiredService<IdentityCoreOptions>().Claims;
        return TryParseKey(principal.FindFirst(claims.UserIdClaimType)?.Value, out var userId)
            && TryParseKey(principal.FindFirst(claims.SessionIdClaimType)?.Value, out var sessionId)
                ? (userId, sessionId)
                : null;
    }

    /// <summary>Names the OIDC client this request's sign-in serves, for the sign-in log.</summary>
    private static void NameClient(IServiceProvider services, string? clientId)
    {
        if (!string.IsNullOrWhiteSpace(clientId) && services.GetService<SignInLogService>() is { } log)
            log.ClientId = clientId;
    }

    /// <summary>Records an attempt refused before <see cref="SignInService"/> judged it, such as a spent email link.</summary>
    private static async Task RecordRefusalAsync(IServiceProvider services, string method, string? subject, string reason, CancellationToken cancellationToken)
    {
        if (services.GetService<SignInLogService>() is { } log)
            await log.RecordAsync(new SignInAttempt { Method = method, Result = SignInResultConstants.Failed, Reason = reason, Subject = subject }, cancellationToken);
    }

    private static async Task<IResult> ListPasskeysAsync(ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, passkeys) = await PasskeysOfAsync(principal, http, cancellationToken);
        var listed = await passkeys.ListAsync(user, cancellationToken);
        return Results.Ok(ApiResponse<IReadOnlyList<PasskeyDto>>.Ok([.. listed.Select(passkey => new PasskeyDto
        {
            Id = passkey.Id,
            Name = passkey.Name,
            CreatedAt = passkey.CreatedAt,
            LastUsedAt = passkey.LastUsedAt,
        })]));
    }

    private static async Task<IResult> PasskeyCreationOptionsAsync(ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, passkeys) = await PasskeysOfAsync(principal, http, cancellationToken);
        return Results.Ok(ApiResponse<PasskeyCeremonyDto>.Ok(CeremonyOf(await passkeys.BeginCreationAsync(user, cancellationToken))));
    }

    private static async Task<IResult> AddPasskeyAsync(PasskeyCreationApiRequest request, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, passkeys) = await PasskeysOfAsync(principal, http, cancellationToken);
        var registered = await passkeys.CompleteCreationAsync(user, request.State, request.Credential.GetRawText(), request.Name, cancellationToken);
        if (!registered.IsSuccess)
            throw registered.ToValidationError().ToException();

        return Results.NoContent();
    }

    private static async Task<IResult> RemovePasskeyAsync(Guid id, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, passkeys) = await PasskeysOfAsync(principal, http, cancellationToken);
        return await passkeys.RemoveAsync(user, id, cancellationToken)
            ? Results.NoContent()
            : throw AppErrorFactory.NotFound("The account has no such passkey.").ToException();
    }

    private static async Task<(IdentityUser User, UserPasskeyService Passkeys)> PasskeysOfAsync(ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var claimType = services.GetRequiredService<IdentityCoreOptions>().Claims.UserIdClaimType;
        var user = await FindAsync(services, principal.FindFirst(claimType)?.Value, cancellationToken)
            ?? throw AppErrorFactory.Unauthorized("The account no longer exists.").ToException();
        return (user, services.GetRequiredService<UserPasskeyService>());
    }

    private static PasskeyCeremonyDto CeremonyOf(PasskeyCeremonyModel ceremony)
        => new() { Options = JsonSerializer.Deserialize<JsonElement>(ceremony.OptionsJson), State = ceremony.State };

    /// <summary>
    /// The 401 a second-factor step answers with: names the method asked for and the available ones, and sends a
    /// delivered method's code when the client chose it or <see cref="TwoFactorOptions.IsCodeSentAutomatically"/> is on. A first
    /// factor that cannot be repeated (a spent link) exposes the ticket for <c>sign-in/two-factor</c>.
    /// </summary>
    private static async Task<AppError> TwoFactorChallengeAsync(
        IServiceProvider services,
        SignInService signIn,
        Guid userId,
        string ticket,
        string method,
        bool chosen,
        CancellationToken cancellationToken,
        bool exposeTicket = false)
    {
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["twoFactorMethod"] = method };
        if (exposeTicket)
        {
            extensions["userId"] = userId.ToString();
            extensions["twoFactorTicket"] = ticket;
        }

        if (services.GetService<UserTwoFactorMethodService>() is { } methods
            && await services.GetRequiredService<UserAccountService>().FindByIdAsync(userId, cancellationToken) is { } user)
        {
            extensions["twoFactorMethods"] = await methods.GetAvailableMethodsAsync(user, cancellationToken);
            var isCodeSentAutomatically = services.GetRequiredService<TwoFactorOptions>().IsCodeSentAutomatically;
            if (!string.Equals(method, TwoFactorMethodNameConstants.Authenticator, StringComparison.OrdinalIgnoreCase) && (chosen || isCodeSentAutomatically))
            {
                var sent = await signIn.SendTwoFactorCodeAsync(userId, ticket, method, cancellationToken: cancellationToken);
                if (sent.Status is not TwoFactorCodeStatus.Sent and not TwoFactorCodeStatus.RateLimited)
                    return FailureOf(sent);

                extensions["isCodeSent"] = sent.IsSent;
                extensions["codeExpiresAt"] = sent.ExpiresAt;
            }
        }

        var metadata = MessageKey(IdentityErrorCodeConstants.TwoFactorRequired);
        metadata[AppErrorProblemDetailsFactory.ExtensionsMetadataKey] = extensions;
        return AppError.Of(AppErrorType.Unauthorized, "Enter your two-factor code.", metadata);
    }

    private static AppError FailureOf(TwoFactorCodeResult result) => result.Status switch
    {
        TwoFactorCodeStatus.RateLimited => AppError.Of(AppErrorType.TooManyRequests, "A code was sent moments ago; wait before asking again.", MessageKey(IdentityErrorCodeConstants.TwoFactorRequired)),
        TwoFactorCodeStatus.DeliveryFailed => AppError.Of(AppErrorType.ExternalUnavailable, "The code could not be delivered; try another method.", MessageKey(IdentityErrorCodeConstants.TwoFactorMethodUnavailable)),
        _ => AppError.Of(AppErrorType.Validation, $"The two-factor method '{result.Method}' is not available for this account.", MessageKey(IdentityErrorCodeConstants.TwoFactorMethodUnavailable)),
    };

    private static async Task<(IdentityUser User, UserTwoFactorService TwoFactor)> TwoFactorOfAsync(ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var claimType = services.GetRequiredService<IdentityCoreOptions>().Claims.UserIdClaimType;
        var user = await FindAsync(services, principal.FindFirst(claimType)?.Value, cancellationToken)
            ?? throw AppErrorFactory.Unauthorized("The account no longer exists.").ToException();
        return (user, services.GetRequiredService<UserTwoFactorService>());
    }

    private static async Task<AccessTokenDto> IssueAsync(
        IServiceProvider services,
        ClaimsPrincipal principal,
        IdentityUser? user,
        string? refreshToken,
        bool isPersistent,
        CancellationToken cancellationToken)
    {
        var issuer = services.GetService<ITokenIssuer>()
            ?? throw BearerUnsupported("Bearer sign-in is not enabled on this instance; sign in with ?useCookies=true.").ToException();
        var lifetime = services.GetService<JwtTokenIssuerOptions>()?.Lifetime ?? TimeSpan.FromHours(1);
        if (refreshToken is null && user is not null && services.GetService<RefreshTokenService>() is { } refresh)
            refreshToken = (await refresh.IssueAsync(user, isPersistent, cancellationToken)).Token;

        return new AccessTokenDto
        {
            AccessToken = issuer.Issue(principal.Claims, new TokenIssuanceContext { Lifetime = lifetime }),
            ExpiresIn = (int)lifetime.TotalSeconds,
            RefreshToken = refreshToken,
        };
    }

    private static async Task<bool> SendConfirmationAsync(IServiceProvider services, IdentityUser user, CancellationToken cancellationToken)
        => services.GetService<UserEmailService>() is { } emails
            && services.GetService<UserAccountMailService>() is { } mail
            && await mail.SendConfirmationAsync(user, emails.IssueConfirmationToken(user), cancellationToken);

    private static async Task<IdentityUser?> FindAsync(IServiceProvider services, string? id, CancellationToken cancellationToken)
        => TryParseKey(id, out var key)
            ? await services.GetRequiredService<UserAccountService>().FindByIdAsync(key, cancellationToken)
            : null;

    private static AppError FailureOf(SignInStatus status) => status switch
    {
        SignInStatus.LockedOut => AppError.Of(AppErrorType.TooManyRequests, "The account is temporarily locked out.", MessageKey(IdentityErrorCodeConstants.LockedOut)),
        SignInStatus.NotAllowed => AppError.Of(AppErrorType.Forbidden, "Confirm your email or phone to sign in.", MessageKey(IdentityErrorCodeConstants.SignInNotAllowed)),
        SignInStatus.RequiresTwoFactor => Unauthorized(IdentityErrorCodeConstants.TwoFactorRequired, "Enter your two-factor code."),
        SignInStatus.PasswordChangeRequired => CodedError(AppErrorType.Forbidden, "Change your password to sign in.", PasswordChangeErrorCodeConstants.ChangeRequired),
        _ => Unauthorized(IdentityErrorCodeConstants.SignInFailed, "The user name, email or password is incorrect."),
    };

    /// <summary>Refuses, before any credential is checked, a bearer sign-in on a host that cannot issue bearer tokens.</summary>
    private static void RequireSessionKind(IServiceProvider services, bool? useCookies)
    {
        if (useCookies != true && services.GetService<ITokenIssuer>() is null)
            throw BearerUnsupported("Bearer sign-in is not enabled on this instance; sign in with ?useCookies=true.").ToException();
    }

    private static AppError BearerUnsupported(string message)
        => CodedError(AppErrorType.Validation, message, AccountApiErrorCodeConstants.BearerSignInUnsupported);

    /// <summary>An error whose identity code a client can read from the problem's <c>identityCode</c> extension, beside the message key translation uses.</summary>
    private static AppError CodedError(AppErrorType type, string message, string code)
    {
        var metadata = MessageKey(code);
        metadata[AppErrorProblemDetailsFactory.ExtensionsMetadataKey] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["identityCode"] = code };
        return AppError.Of(type, message, metadata);
    }

    private static AppError Unauthorized(string code, string message) => AppError.Of(AppErrorType.Unauthorized, message, MessageKey(code));

    private static Dictionary<string, object?> MessageKey(string code)
        => new(StringComparer.Ordinal) { ["messageKey"] = code, ["identityCode"] = code };

    /// <summary>A JSON body when the request carries one, else null; malformed JSON answers <c>400</c>.</summary>
    private static async Task<T?> ReadOptionalBodyAsync<T>(HttpContext http, CancellationToken cancellationToken)
        where T : class
    {
        if (!http.Request.HasJsonContentType() || http.Request.ContentLength == 0)
            return null;

        try
        {
            return await http.Request.ReadFromJsonAsync<T>(cancellationToken);
        }
        catch (JsonException)
        {
            throw AppError.Of(AppErrorType.Validation, "The request body is not valid JSON.").ToException();
        }
    }

    private static bool TryParseKey(string? value, out Guid key) => Guid.TryParse(value, out key);

    private static string IdOf(IdentityUser user) => user.Id.ToString();
}

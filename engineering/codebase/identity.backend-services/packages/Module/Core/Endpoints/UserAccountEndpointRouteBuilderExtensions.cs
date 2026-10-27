using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Emails;
using WoW.Two.Sdk.Beta.Be.Identity.Core.EmailSignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passkeys;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;
using WoW.Two.Sdk.Beta.Be.Identity.Core.RefreshTokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.TwoFactor;
using WoW.Two.Sdk.Beta.Be.Identity.Jwt.Issuance;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Factories;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>Maps the account HTTP API over the registered identity slices.</summary>
public static class UserAccountEndpointRouteBuilderExtensions
{
    private static readonly JsonSerializerOptions ExportJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// Maps <c>register</c>, <c>login</c>, <c>refresh</c>, <c>logout</c>, <c>confirm-email</c>,
    /// <c>resend-confirmation-email</c>, <c>forgot-password</c>, <c>reset-password</c>, <c>manage/info</c> and, with the
    /// two-factor slice registered, <c>manage/2fa</c> (status, authenticator, enable, disable, recovery-codes, preferred,
    /// methods/{method}/send, methods/{method}/enable, plus <c>login/two-factor</c>); with email sign-in registered,
    /// <c>email-sign-in/send</c> and <c>email-sign-in</c>; with personal data registered, <c>manage/personal-data</c> and
    /// <c>manage/delete-account</c>; and, with the passkey slice registered, <c>passkeys/login/options</c>,
    /// <c>passkeys/login</c> and <c>manage/passkeys</c> (list, options, register, remove) under
    /// <paramref name="endpoints"/> — mount it on a group such as <c>app.MapGroup("/account")</c>. Sign-in returns a bearer
    /// session (JWT via <see cref="ITokenIssuer"/>, plus a refresh token when registered) or, with <c>?useCookies=true</c>,
    /// a cookie. Failures surface as problem details, translated when error translation is enabled.
    /// </summary>
    /// <param name="endpoints">The route builder or group.</param>
    public static IEndpointRouteBuilder MapUserAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/register", RegisterAsync);
        endpoints.MapPost("/login", SignInAsync);
        endpoints.MapPost("/refresh", RefreshAsync);
        endpoints.MapPost("/logout", SignOutAsync);
        endpoints.MapGet("/confirm-email", ConfirmEmailAsync);
        endpoints.MapPost("/resend-confirmation-email", ResendConfirmationAsync);
        endpoints.MapPost("/forgot-password", ForgotPasswordAsync);
        endpoints.MapPost("/reset-password", ResetPasswordAsync);
        endpoints.MapGet("/manage/info", InfoAsync).RequireAuthorization();

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
            endpoints.MapPost("/login/two-factor", TwoFactorLoginAsync);
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
            endpoints.MapPost("/passkeys/login/options", PasskeySignInOptions);
            endpoints.MapPost("/passkeys/login", PasskeySignInAsync);
            endpoints.MapGet("/manage/passkeys", ListPasskeysAsync).RequireAuthorization();
            endpoints.MapPost("/manage/passkeys/options", PasskeyRegistrationOptionsAsync).RequireAuthorization();
            endpoints.MapPost("/manage/passkeys", RegisterPasskeyAsync).RequireAuthorization();
            endpoints.MapDelete("/manage/passkeys/{id:guid}", RemovePasskeyAsync).RequireAuthorization();
        }

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(RegisterAccountApiRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var user = new IdentityUser { UserName = string.IsNullOrWhiteSpace(request.UserName) ? request.Email : request.UserName, Email = request.Email };
        var created = await services.GetRequiredService<UserPasswordService>().CreateWithPasswordAsync(user, request.Password, cancellationToken);
        if (!created.Succeeded)
            throw created.ToValidationError().ToException();

        var sent = await SendConfirmationAsync(services, user, cancellationToken);
        return Results.Ok(ApiResponse<RegisteredAccountDto>.Ok(new RegisteredAccountDto { UserId = IdOf(user), ConfirmationSent = sent }));
    }

    private static async Task<IResult> SignInAsync(SignInApiRequest request, bool? useCookies, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var signIn = services.GetRequiredService<SignInService>();
        var result = await signIn.PasswordSignInAsync(request.Login, request.Password, cancellationToken);
        if (result is { Status: SignInStatus.RequiresTwoFactor, TwoFactorTicket: { } ticket } && TryParseKey(result.UserId, out var userId))
        {
            var method = request.TwoFactorMethod ?? result.TwoFactorMethod ?? TwoFactorMethodNameConstants.Authenticator;
            if (!string.IsNullOrWhiteSpace(request.TwoFactorCode))
                result = await signIn.TwoFactorSignInAsync(userId, ticket, method, request.TwoFactorCode, cancellationToken);
            else if (!string.IsNullOrWhiteSpace(request.RecoveryCode))
                result = await signIn.RecoveryCodeSignInAsync(userId, ticket, request.RecoveryCode, cancellationToken);
            else
                throw (await TwoFactorChallengeAsync(services, signIn, userId, ticket, method, request.TwoFactorMethod is not null, cancellationToken)).ToException();
        }

        return await SessionAsync(result, useCookies, http, cancellationToken);
    }

    private static IResult PasskeySignInOptions(HttpContext http)
        => Results.Ok(ApiResponse<PasskeyCeremonyDto>.Ok(CeremonyOf(http.RequestServices.GetRequiredService<UserPasskeyService>().BeginSignIn())));

    private static async Task<IResult> PasskeySignInAsync(PasskeySignInApiRequest request, bool? useCookies, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var result = await services.GetRequiredService<SignInService>().PasskeySignInAsync(request.State, request.Credential.GetRawText(), cancellationToken);
        if (result.Status == SignInStatus.Failed)
            throw Unauthorized(IdentityErrorCodeConstants.InvalidPasskey, "The passkey could not be verified; try again.").ToException();

        return await SessionAsync(result, useCookies, http, cancellationToken);
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

    private static async Task<IResult> EmailSignInAsync(EmailSignInApiRequest request, bool? useCookies, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var emailSignIn = services.GetRequiredService<UserEmailSignInService>();
        var user = !string.IsNullOrWhiteSpace(request.Token)
            ? await emailSignIn.VerifyLinkTokenAsync(request.Email, request.Token, cancellationToken)
            : await emailSignIn.VerifyCodeAsync(request.Email, request.Code ?? string.Empty, cancellationToken);
        if (user is null)
            throw Unauthorized(IdentityErrorCodeConstants.InvalidEmailSignIn, "The link or code is invalid or has expired.").ToException();

        var signIn = services.GetRequiredService<SignInService>();
        var result = await signIn.SignInAsync(user, [AuthenticationMethodConstants.OneTimePassword], cancellationToken);
        if (result is { Status: SignInStatus.RequiresTwoFactor, TwoFactorTicket: { } ticket } && TryParseKey(result.UserId, out var userId))
        {
            var method = result.TwoFactorMethod ?? TwoFactorMethodNameConstants.Authenticator;
            throw (await TwoFactorChallengeAsync(services, signIn, userId, ticket, method, chosen: false, cancellationToken, exposeTicket: true)).ToException();
        }

        return await SessionAsync(result, useCookies, http, cancellationToken);
    }

    private static async Task<IResult> TwoFactorLoginAsync(TwoFactorLoginApiRequest request, bool? useCookies, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        if (!TryParseKey(request.UserId, out var userId))
            throw Unauthorized(IdentityErrorCodeConstants.SignInFailed, "The sign-in has expired; start again.").ToException();

        var signIn = services.GetRequiredService<SignInService>();
        var method = request.TwoFactorMethod ?? TwoFactorMethodNameConstants.Authenticator;
        SignInResult result;
        if (!string.IsNullOrWhiteSpace(request.TwoFactorCode))
            result = await signIn.TwoFactorSignInAsync(userId, request.Ticket, method, request.TwoFactorCode, cancellationToken);
        else if (!string.IsNullOrWhiteSpace(request.RecoveryCode))
            result = await signIn.RecoveryCodeSignInAsync(userId, request.Ticket, request.RecoveryCode, cancellationToken);
        else
            throw (await TwoFactorChallengeAsync(services, signIn, userId, request.Ticket, method, request.TwoFactorMethod is not null, cancellationToken, exposeTicket: true)).ToException();

        return await SessionAsync(result, useCookies, http, cancellationToken);
    }

    /// <summary>The session of a successful sign-in: a cookie with <c>useCookies</c>, else a bearer token pair.</summary>
    private static async Task<IResult> SessionAsync(SignInResult result, bool? useCookies, HttpContext http, CancellationToken cancellationToken)
    {
        if (!result.Succeeded)
            throw FailureOf(result.Status).ToException();

        if (useCookies == true)
        {
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, result.Principal!);
            return Results.NoContent();
        }

        var services = http.RequestServices;
        var user = await FindAsync(services, result.UserId, cancellationToken);
        return Results.Ok(ApiResponse<AccessTokenDto>.Ok(await IssueAsync(services, result.Principal!, user, null, cancellationToken)));
    }

    private static async Task<IResult> RefreshAsync(RefreshAccessApiRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var refresh = services.GetService<RefreshTokenService>()
            ?? throw new NotSupportedException("Refreshing needs the refresh-token slice; call .AddRefreshTokens().");
        var redeemed = await refresh.RedeemAsync(request.RefreshToken, cancellationToken);
        if (!redeemed.Succeeded)
            throw Unauthorized(IdentityErrorCodeConstants.InvalidRefreshToken, "The session has expired; sign in again.").ToException();

        var renewed = await services.GetRequiredService<SignInService>().RenewAsync(redeemed.User!, cancellationToken);
        if (!renewed.Succeeded)
        {
            await refresh.RevokeAsync(redeemed.Token!.Token, cancellationToken);
            throw FailureOf(renewed.Status).ToException();
        }

        return Results.Ok(ApiResponse<AccessTokenDto>.Ok(await IssueAsync(services, renewed.Principal!, redeemed.User, redeemed.Token!.Token, cancellationToken)));
    }

    private static async Task<IResult> SignOutAsync(SignOutApiRequest? request, HttpContext http, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request?.RefreshToken) && http.RequestServices.GetService<RefreshTokenService>() is { } refresh)
            await refresh.RevokeAsync(request.RefreshToken, cancellationToken);

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
        if (!confirmed.Succeeded)
            throw confirmed.ToValidationError().ToException();

        return Results.NoContent();
    }

    private static async Task<IResult> ResendConfirmationAsync(EmailAddressApiRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var user = await services.GetRequiredService<UserAccountService>().FindByEmailAsync(request.Email, cancellationToken);
        if (user is { EmailConfirmed: false })
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
        if (!reset.Succeeded)
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
            EmailConfirmed = user.EmailConfirmed,
            TwoFactorEnabled = user.TwoFactorEnabled,
        }));
    }

    private static async Task<IResult> TwoFactorStatusAsync(ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, twoFactor) = await TwoFactorOfAsync(principal, http, cancellationToken);
        var methods = http.RequestServices.GetService<UserTwoFactorMethodService>();
        return Results.Ok(ApiResponse<TwoFactorStatusDto>.Ok(new TwoFactorStatusDto
        {
            Enabled = user.TwoFactorEnabled,
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
        if (!enabled.Succeeded)
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
        if (!user.TwoFactorEnabled)
            throw AppError.Of(AppErrorType.Conflict, "Enable two-factor before generating recovery codes.", MessageKey(IdentityErrorCodeConstants.AuthenticatorNotConfigured)).ToException();

        var codes = await twoFactor.GenerateRecoveryCodesAsync(user, cancellationToken);
        return Results.Ok(ApiResponse<RecoveryCodesDto>.Ok(new RecoveryCodesDto { RecoveryCodes = codes }));
    }

    private static async Task<IResult> SetPreferredMethodAsync(TwoFactorMethodApiRequest request, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, _) = await TwoFactorOfAsync(principal, http, cancellationToken);
        var chosen = await http.RequestServices.GetRequiredService<UserTwoFactorMethodService>().SetPreferredMethodAsync(user, request.Method, cancellationToken);
        if (!chosen.Succeeded)
            throw chosen.ToValidationError().ToException();

        return Results.NoContent();
    }

    private static async Task<IResult> SendMethodCodeAsync(string method, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, _) = await TwoFactorOfAsync(principal, http, cancellationToken);
        var sent = await http.RequestServices.GetRequiredService<UserTwoFactorMethodService>().SendCodeAsync(user, method, cancellationToken: cancellationToken);
        return sent.Sent
            ? Results.Ok(ApiResponse<TwoFactorCodeSentDto>.Ok(new TwoFactorCodeSentDto { Method = sent.Method, ExpiresAt = sent.ExpiresAt }))
            : throw FailureOf(sent).ToException();
    }

    private static async Task<IResult> EnableMethodAsync(string method, TwoFactorCodeApiRequest request, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, twoFactor) = await TwoFactorOfAsync(principal, http, cancellationToken);
        var enabled = await http.RequestServices.GetRequiredService<UserTwoFactorMethodService>().EnableAsync(user, method, request.Code, cancellationToken);
        if (!enabled.Succeeded)
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

    private static async Task<IResult> PasskeyRegistrationOptionsAsync(ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, passkeys) = await PasskeysOfAsync(principal, http, cancellationToken);
        return Results.Ok(ApiResponse<PasskeyCeremonyDto>.Ok(CeremonyOf(await passkeys.BeginRegistrationAsync(user, cancellationToken))));
    }

    private static async Task<IResult> RegisterPasskeyAsync(PasskeyRegistrationApiRequest request, ClaimsPrincipal principal, HttpContext http, CancellationToken cancellationToken)
    {
        var (user, passkeys) = await PasskeysOfAsync(principal, http, cancellationToken);
        var registered = await passkeys.CompleteRegistrationAsync(user, request.State, request.Credential.GetRawText(), request.Name, cancellationToken);
        if (!registered.Succeeded)
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
    /// delivered method's code when the client chose it or <see cref="TwoFactorOptions.AutoSendCode"/> is on. A first
    /// factor that cannot be repeated (a spent link) exposes the ticket for <c>login/two-factor</c>.
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
            var autoSend = services.GetRequiredService<TwoFactorOptions>().AutoSendCode;
            if (!string.Equals(method, TwoFactorMethodNameConstants.Authenticator, StringComparison.OrdinalIgnoreCase) && (chosen || autoSend))
            {
                var sent = await signIn.SendTwoFactorCodeAsync(userId, ticket, method, cancellationToken: cancellationToken);
                if (sent.Status is not TwoFactorCodeStatus.Sent and not TwoFactorCodeStatus.RateLimited)
                    return FailureOf(sent);

                extensions["codeSent"] = sent.Sent;
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

    private static async Task<AccessTokenDto> IssueAsync(IServiceProvider services, ClaimsPrincipal principal, IdentityUser? user, string? refreshToken, CancellationToken cancellationToken)
    {
        var issuer = services.GetService<ITokenIssuer>()
            ?? throw new NotSupportedException("Bearer sign-in needs JWT issuance; call AddJwtTokenIssuance(...) or sign in with ?useCookies=true.");
        var lifetime = services.GetService<JwtTokenIssuerOptions>()?.Lifetime ?? TimeSpan.FromHours(1);
        if (refreshToken is null && user is not null && services.GetService<RefreshTokenService>() is { } refresh)
            refreshToken = (await refresh.IssueAsync(user, cancellationToken)).Token;

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
        _ => Unauthorized(IdentityErrorCodeConstants.SignInFailed, "The login or password is incorrect."),
    };

    private static AppError Unauthorized(string code, string message) => AppError.Of(AppErrorType.Unauthorized, message, MessageKey(code));

    private static Dictionary<string, object?> MessageKey(string code)
        => new(StringComparer.Ordinal) { ["messageKey"] = code, ["identityCode"] = code };

    private static bool TryParseKey(string? value, out Guid key) => Guid.TryParse(value, out key);

    private static string IdOf(IdentityUser user) => user.Id.ToString();
}

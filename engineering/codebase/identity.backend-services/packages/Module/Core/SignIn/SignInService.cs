using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Lockout;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passkeys;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core.TwoFactor;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;

/// <summary>
/// Provides sign-in orchestration: lockout → password → preconditions → second factor → principal. The result carries a
/// principal the host issues as a cookie or a JWT; the session shape stays the host's choice. Lockout and two-factor
/// steps apply when their slices are registered; a user with two-factor enabled always needs the second factor.
/// </summary>
/// <remarks>
///   - a password that matches its hash is then checked against the current password rules, once the password change slice
///     is registered; one that fails them ends as <see cref="SignInStatus.PasswordChangeRequired"/> with a ticket for the
///     change routes, and no principal: the old hash stays until the person changes it
///   - the principal records how the person signed in as <see cref="AuthenticationMethodConstants.ClaimType"/> claims:
///     <c>pwd</c> for a password, <c>hwk</c> for a passkey, <c>otp</c> and <c>mfa</c> after a second factor, and whatever
///     the caller names for a sign-in completed elsewhere; a renewal records none
///   - with the sign-in log slice, every attempt it judges is recorded, whatever its outcome, and the result carries the
///     row's id; a renewal is not an attempt
///   - an account without a password answers a password attempt as an unknown identifier does: the same hash work, and
///     no failed attempt counted, so nobody can lock out a phone or passkey user by guessing a password they never had
///   - a lockout refuses new sign-ins only; a renewal of an existing session passes it, so failed guesses by someone else
///     never end the person's sessions
///   - a refused precondition names itself in <see cref="SignInResult.UnmetPrecondition"/>; an account without an email
///     but with a confirmed phone (a phone sign-in's person) meets the confirmed-email precondition through its phone
/// </remarks>
public sealed class SignInService
{
    private static readonly SignInResult FailedResult = new() { Status = SignInStatus.Failed };
    private static readonly SignInResult LockedOutResult = new() { Status = SignInStatus.LockedOut };
    private static readonly string[] PasswordMethods = [AuthenticationMethodConstants.Password];
    private static readonly string[] PasskeyMethods = [AuthenticationMethodConstants.HardwareKey];
    private static readonly string[] SecondFactorMethods = [AuthenticationMethodConstants.OneTimePassword, AuthenticationMethodConstants.MultipleFactors];

    private readonly UserAccountService _accounts;
    private readonly UserClaimsPrincipalFactory _principals;
    private readonly SignInOptions _options;
    private readonly UserPasswordService? _passwords;
    private readonly IPasswordHasher<IdentityUser>? _hasher;
    private readonly UserLockoutService? _lockout;
    private readonly UserTokenIssuer? _tokens;
    private readonly UserTwoFactorService? _twoFactor;
    private readonly UserTwoFactorMethodService? _twoFactorMethods;
    private readonly UserPasskeyService? _passkeys;
    private readonly SignInLogService? _signInLog;
    private readonly UserPasswordChangeService? _passwordChange;
    private static string? s_decoyHash;

    /// <summary>Create the service over the registered slices.</summary>
    /// <param name="accounts">The core account service.</param>
    /// <param name="principals">The principal factory.</param>
    /// <param name="options">Identity options carrying the sign-in preconditions.</param>
    /// <param name="passwords">The password slice; null disables password sign-in.</param>
    /// <param name="hasher">The password hasher, used to spend equal time on unknown identifiers.</param>
    /// <param name="lockout">The lockout slice; null skips lockout.</param>
    /// <param name="tokens">The purpose-token issuer; null omits two-factor tickets.</param>
    /// <param name="twoFactor">The two-factor slice; null disables the second-factor methods.</param>
    /// <param name="twoFactorMethods">The delivered-code methods of the two-factor slice; null leaves only the authenticator.</param>
    /// <param name="passkeys">The passkey slice; null disables passkey sign-in.</param>
    /// <param name="signInLog">The sign-in log; null records nothing.</param>
    /// <param name="passwordChange">The password change slice; null skips the check of a matched password against the current rules.</param>
    public SignInService(
        UserAccountService accounts,
        UserClaimsPrincipalFactory principals,
        IdentityCoreOptions options,
        UserPasswordService? passwords = null,
        IPasswordHasher<IdentityUser>? hasher = null,
        UserLockoutService? lockout = null,
        UserTokenIssuer? tokens = null,
        UserTwoFactorService? twoFactor = null,
        UserTwoFactorMethodService? twoFactorMethods = null,
        UserPasskeyService? passkeys = null,
        SignInLogService? signInLog = null,
        UserPasswordChangeService? passwordChange = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _accounts = accounts;
        _principals = principals;
        _options = options.SignIn;
        _passwords = passwords;
        _hasher = hasher;
        _lockout = lockout;
        _tokens = tokens;
        _twoFactor = twoFactor;
        _twoFactorMethods = twoFactorMethods;
        _passkeys = passkeys;
        _signInLog = signInLog;
        _passwordChange = passwordChange;
    }

    /// <summary>Sign in with a user name (or email, when allowed) and password.</summary>
    /// <param name="identifier">The user name or email.</param>
    /// <param name="password">The password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="NotSupportedException">The password slice is not registered.</exception>
    public async Task<SignInResult> PasswordSignInAsync(string identifier, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(password);

        var user = await _accounts.FindByNameAsync(identifier, cancellationToken);
        if (user is null && _options.IsEmailIdentifierAllowed && identifier.Contains('@', StringComparison.Ordinal))
            user = await _accounts.FindByEmailAsync(identifier, cancellationToken);

        if (user is not null)
            return await PasswordSignInCoreAsync(user, password, identifier, cancellationToken);

        SpendDecoyVerification(password);
        return await LogAsync(FailedResult, SignInMethodConstants.Password, null, identifier, SignInReasonConstants.UnknownAccount, cancellationToken);
    }

    /// <summary>Sign in <paramref name="user"/> with a password.</summary>
    /// <param name="user">The user.</param>
    /// <param name="password">The password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="NotSupportedException">The password slice is not registered.</exception>
    public Task<SignInResult> PasswordSignInAsync(IdentityUser user, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        return PasswordSignInCoreAsync(user, password, user.UserName, cancellationToken);
    }

    private async Task<SignInResult> PasswordSignInCoreAsync(IdentityUser user, string password, string? subject, CancellationToken cancellationToken)
    {
        var passwords = _passwords ?? throw new NotSupportedException("Password sign-in needs the password slice; call .AddArgon2Passwords().");
        const string method = SignInMethodConstants.Password;

        // No password to guess: answer as an unknown identifier, in the same time, and count nothing toward a lockout.
        if (!passwords.HasPassword(user))
        {
            SpendDecoyVerification(password);
            return await LogAsync(FailedResult, method, user, subject, SignInReasonConstants.NoPassword, cancellationToken);
        }

        if (_lockout?.IsLockedOut(user) == true)
            return await LogAsync(LockedOutResult, method, user, subject, SignInReasonConstants.LockedOut, cancellationToken);

        if (!await passwords.CheckPasswordAsync(user, password, cancellationToken))
        {
            return _lockout is not null && await _lockout.RecordFailedAttemptAsync(user, cancellationToken)
                ? await LogAsync(LockedOutResult, method, user, subject, SignInReasonConstants.LockoutStarted, cancellationToken)
                : await LogAsync(FailedResult, method, user, subject, SignInReasonConstants.WrongPassword, cancellationToken);
        }

        // The hash matched, so the typed password is known; this is the only moment it can be checked against the current rules.
        var first = await CompleteFirstFactorAsync(user, PasswordMethods, cancellationToken);
        var isChangeRequired = _passwordChange is not null
            && (first.Status is SignInStatus.Succeeded or SignInStatus.RequiresTwoFactor)
            && await _passwordChange.IsChangeRequiredAsync(user, password, cancellationToken);
        if (isChangeRequired && first.Status == SignInStatus.RequiresTwoFactor)
            return await LogAsync(first with { IsPasswordChangeRequired = true }, method, user, subject, null, cancellationToken);
        if (isChangeRequired)
            return await LogAsync(RequiredChange(user), method, user, subject, SignInReasonConstants.PasswordBreaksRules, cancellationToken);

        return await LogAsync(first, method, user, subject, null, cancellationToken);
    }

    /// <summary>
    /// Finish a password sign-in whose second factor was completed in the same request: when the password matched but breaks
    /// the current rules (<see cref="SignInResult.IsPasswordChangeRequired"/> on <paramref name="firstFactor"/>), the success
    /// becomes <see cref="SignInStatus.PasswordChangeRequired"/> and no session or token follows.
    /// </summary>
    /// <param name="firstFactor">The result of <see cref="PasswordSignInAsync(string, string, CancellationToken)"/>, which asked for the second factor.</param>
    /// <param name="completed">The result of the second-factor step that followed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SignInResult> FinishPasswordSignInAsync(SignInResult firstFactor, SignInResult completed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(firstFactor);
        ArgumentNullException.ThrowIfNull(completed);
        if (!firstFactor.IsPasswordChangeRequired || !completed.IsSuccess || _passwordChange is null
            || !Guid.TryParse(completed.UserId, out var userId) || await _accounts.FindByIdAsync(userId, cancellationToken) is not { } user)
            return completed;

        return await LogAsync(RequiredChange(user), SignInMethodConstants.Password, user, user.Email ?? user.UserName, SignInReasonConstants.PasswordBreaksRules, cancellationToken);
    }

    private SignInResult RequiredChange(IdentityUser user)
        => new()
        {
            Status = SignInStatus.PasswordChangeRequired,
            UserId = IdOf(user),
            PasswordChangeTicket = _passwordChange!.IssueRequiredChangeTicket(user),
        };

    /// <summary>Sign in a user already authenticated elsewhere (external account, passwordless code); lockout and preconditions still apply.</summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<SignInResult> SignInAsync(IdentityUser user, CancellationToken cancellationToken = default)
        => SignInAsync(user, [], cancellationToken);

    /// <summary>
    /// Sign in a user already authenticated elsewhere, recording how as <see cref="AuthenticationMethodConstants.ClaimType"/>
    /// claims; lockout and preconditions still apply.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="methods">The methods used, such as <see cref="AuthenticationMethodConstants.OneTimePassword"/> for an emailed code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<SignInResult> SignInAsync(IdentityUser user, IReadOnlyCollection<string> methods, CancellationToken cancellationToken = default)
        => SignInAsync(user, methods, SignInMethodConstants.External, cancellationToken);

    /// <summary>
    /// Sign in a user already authenticated elsewhere, recording how as <see cref="AuthenticationMethodConstants.ClaimType"/>
    /// claims and the step as <paramref name="logMethod"/> in the sign-in log; lockout and preconditions still apply.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="methods">The methods used, such as <see cref="AuthenticationMethodConstants.OneTimePassword"/> for an emailed code.</param>
    /// <param name="logMethod">The step the log names, such as <see cref="SignInMethodConstants.EmailLink"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SignInResult> SignInAsync(IdentityUser user, IReadOnlyCollection<string> methods, string logMethod, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentException.ThrowIfNullOrWhiteSpace(logMethod);
        return _lockout?.IsLockedOut(user) == true
            ? await LogAsync(LockedOutResult, logMethod, user, user.Email ?? user.UserName, SignInReasonConstants.LockedOut, cancellationToken)
            : await LogAsync(await CompleteFirstFactorAsync(user, methods, cancellationToken), logMethod, user, user.Email ?? user.UserName, null, cancellationToken);
    }

    /// <summary>
    /// Sign in with a passkey: the assertion is verified, then lockout and preconditions apply. A passkey is
    /// phishing-resistant and user-verified, so no second factor follows.
    /// </summary>
    /// <param name="state">The state from <c>UserPasskeyService.BeginSignIn</c>.</param>
    /// <param name="credentialJson">The <c>PublicKeyCredential</c> JSON from <c>navigator.credentials.get</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="NotSupportedException">The passkey slice is not registered.</exception>
    public async Task<SignInResult> PasskeySignInAsync(string state, string credentialJson, CancellationToken cancellationToken = default)
    {
        var passkeys = _passkeys ?? throw new NotSupportedException("Passkey sign-in needs the passkey slice; call .AddPasskeys(...).");
        const string method = SignInMethodConstants.Passkey;
        var user = await passkeys.VerifySignInAsync(state, credentialJson, cancellationToken);
        if (user is null)
            return await LogAsync(FailedResult, method, null, null, SignInReasonConstants.InvalidPasskey, cancellationToken);
        if (_lockout?.IsLockedOut(user) == true)
            return await LogAsync(LockedOutResult, method, user, null, SignInReasonConstants.LockedOut, cancellationToken);
        if (UnmetPrecondition(user) is { } unmet)
            return await LogAsync(NotAllowed(user, unmet), method, user, null, null, cancellationToken);

        return await LogAsync(await SucceedAsync(user, PasskeyMethods, cancellationToken), method, user, null, null, cancellationToken);
    }

    /// <summary>
    /// Renew the session of a user who already completed sign-in (refresh-token redemption): the preconditions apply
    /// again; a lockout and the second factor do not, since a lockout guards new credential attempts only.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SignInResult> RenewAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (UnmetPrecondition(user) is { } unmet)
            return NotAllowed(user, unmet);

        return new SignInResult
        {
            Status = SignInStatus.Succeeded,
            UserId = IdOf(user),
            Principal = await _principals.CreateAsync(user, cancellationToken: cancellationToken),
        };
    }

    /// <summary>Complete a sign-in with an authenticator code, after a <see cref="SignInStatus.RequiresTwoFactor"/> result.</summary>
    /// <param name="userId">The user id from that result.</param>
    /// <param name="ticket">The two-factor ticket from that result.</param>
    /// <param name="code">The authenticator code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="NotSupportedException">The two-factor slice or purpose tokens are not registered.</exception>
    public Task<SignInResult> TwoFactorSignInAsync(Guid userId, string ticket, string code, CancellationToken cancellationToken = default)
        => SecondFactorSignInAsync(
            userId,
            ticket,
            SignInMethodConstants.SecondFactor(TwoFactorMethodNameConstants.Authenticator),
            (factor, user) => factor.VerifyAuthenticatorCodeAsync(user, code, cancellationToken),
            cancellationToken);

    /// <summary>Complete a sign-in with a code for <paramref name="method"/> — the authenticator or a delivered method — after a <see cref="SignInStatus.RequiresTwoFactor"/> result.</summary>
    /// <param name="userId">The user id from that result.</param>
    /// <param name="ticket">The two-factor ticket from that result.</param>
    /// <param name="method">The method the code belongs to, such as <c>sms</c>.</param>
    /// <param name="code">The code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="NotSupportedException">The two-factor slice or purpose tokens are not registered.</exception>
    public Task<SignInResult> TwoFactorSignInAsync(Guid userId, string ticket, string method, string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        return _twoFactorMethods is null || string.Equals(method, TwoFactorMethodNameConstants.Authenticator, StringComparison.OrdinalIgnoreCase)
            ? TwoFactorSignInAsync(userId, ticket, code, cancellationToken)
            : SecondFactorSignInAsync(
                userId,
                ticket,
                SignInMethodConstants.SecondFactor(method),
                (_, user) => _twoFactorMethods.VerifyCodeAsync(user, method, code, cancellationToken),
                cancellationToken);
    }

    /// <summary>
    /// Send the code of <paramref name="method"/> (or the preferred method) to a user holding a two-factor ticket; a
    /// ticket that does not verify reports <see cref="TwoFactorCodeStatus.Unavailable"/>.
    /// </summary>
    /// <param name="userId">The user id from the <see cref="SignInStatus.RequiresTwoFactor"/> result.</param>
    /// <param name="ticket">The two-factor ticket from that result.</param>
    /// <param name="method">The method to send; null takes the preferred method.</param>
    /// <param name="culture">The recipient's culture for the wording; null takes the current UI culture.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="NotSupportedException">The two-factor slice or purpose tokens are not registered.</exception>
    public async Task<TwoFactorCodeResult> SendTwoFactorCodeAsync(Guid userId, string ticket, string? method = null, CultureInfo? culture = null, CancellationToken cancellationToken = default)
    {
        var methods = _twoFactorMethods ?? throw new NotSupportedException("Delivered two-factor codes need the two-factor slice; call .AddTwoFactor().");
        var tokens = _tokens ?? throw new NotSupportedException("Two-factor sign-in needs purpose tokens; call .AddUserTokens(...).");

        var user = await _accounts.FindByIdAsync(userId, cancellationToken);
        if (user is null || !tokens.Verify(user, UserTokenPurposeConstants.TwoFactorSignIn, ticket))
            return new TwoFactorCodeResult { Status = TwoFactorCodeStatus.Unavailable, Method = method ?? string.Empty };

        method ??= await methods.GetPreferredMethodAsync(user, cancellationToken);
        return method is null
            ? new TwoFactorCodeResult { Status = TwoFactorCodeStatus.Unavailable, Method = string.Empty }
            : await methods.SendCodeAsync(user, method, culture, cancellationToken);
    }

    /// <summary>Complete a sign-in with a recovery code, which is consumed.</summary>
    /// <param name="userId">The user id from the <see cref="SignInStatus.RequiresTwoFactor"/> result.</param>
    /// <param name="ticket">The two-factor ticket from that result.</param>
    /// <param name="recoveryCode">One unused recovery code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="NotSupportedException">The two-factor slice or purpose tokens are not registered.</exception>
    public Task<SignInResult> RecoveryCodeSignInAsync(Guid userId, string ticket, string recoveryCode, CancellationToken cancellationToken = default)
        => SecondFactorSignInAsync(
            userId,
            ticket,
            SignInMethodConstants.RecoveryCode,
            async (factor, user) => (await factor.RedeemRecoveryCodeAsync(user, recoveryCode, cancellationToken)).IsSuccess,
            cancellationToken);

    private async Task<SignInResult> SecondFactorSignInAsync(
        Guid userId,
        string ticket,
        string method,
        Func<UserTwoFactorService, IdentityUser, Task<bool>> verify,
        CancellationToken cancellationToken)
    {
        var twoFactor = _twoFactor ?? throw new NotSupportedException("Two-factor sign-in needs the two-factor slice; call .AddTwoFactor().");
        var tokens = _tokens ?? throw new NotSupportedException("Two-factor sign-in needs purpose tokens; call .AddUserTokens(...).");

        var user = await _accounts.FindByIdAsync(userId, cancellationToken);
        if (user is null || !tokens.Verify(user, UserTokenPurposeConstants.TwoFactorSignIn, ticket))
            return await LogAsync(FailedResult, method, user, null, SignInReasonConstants.InvalidTicket, cancellationToken);
        if (_lockout?.IsLockedOut(user) == true)
            return await LogAsync(LockedOutResult, method, user, null, SignInReasonConstants.LockedOut, cancellationToken);

        if (!await verify(twoFactor, user))
        {
            return _lockout is not null && await _lockout.RecordFailedAttemptAsync(user, cancellationToken)
                ? await LogAsync(LockedOutResult, method, user, null, SignInReasonConstants.LockoutStarted, cancellationToken)
                : await LogAsync(FailedResult, method, user, null, SignInReasonConstants.WrongCode, cancellationToken);
        }

        return await LogAsync(await SucceedAsync(user, SecondFactorMethods, cancellationToken), method, user, null, null, cancellationToken);
    }

    private async Task<SignInResult> CompleteFirstFactorAsync(IdentityUser user, IReadOnlyCollection<string> methods, CancellationToken cancellationToken)
    {
        if (UnmetPrecondition(user) is { } unmet)
            return NotAllowed(user, unmet);

        if (user.IsTwoFactorEnabled)
        {
            return new SignInResult
            {
                Status = SignInStatus.RequiresTwoFactor,
                UserId = IdOf(user),
                TwoFactorTicket = _tokens?.Issue(user, UserTokenPurposeConstants.TwoFactorSignIn),
                TwoFactorMethod = _twoFactorMethods is null
                    ? TwoFactorMethodNameConstants.Authenticator
                    : await _twoFactorMethods.GetPreferredMethodAsync(user, cancellationToken) ?? TwoFactorMethodNameConstants.Authenticator,
            };
        }

        return await SucceedAsync(user, methods, cancellationToken);
    }

    private async Task<SignInResult> SucceedAsync(IdentityUser user, IReadOnlyCollection<string> methods, CancellationToken cancellationToken)
    {
        if (_lockout is not null)
            await _lockout.ResetFailedAttemptsAsync(user, cancellationToken);

        var principal = await _principals.CreateAsync(user, cancellationToken: cancellationToken);
        if (principal.Identity is ClaimsIdentity identity)
        {
            foreach (var method in methods.Distinct(StringComparer.Ordinal))
                identity.AddClaim(new Claim(AuthenticationMethodConstants.ClaimType, method));
        }

        return new SignInResult { Status = SignInStatus.Succeeded, UserId = IdOf(user), Principal = principal };
    }

    /// <summary>
    /// The sign-in precondition <paramref name="user"/> misses, as a log reason; null when every one is met. An account
    /// with no email but a confirmed phone meets the confirmed-email precondition through its phone: a phone sign-in's
    /// person has no email to confirm.
    /// </summary>
    private string? UnmetPrecondition(IdentityUser user)
        => _options.IsConfirmedEmailRequired && !user.IsEmailConfirmed && !(string.IsNullOrEmpty(user.Email) && user.IsPhoneNumberConfirmed)
            ? SignInReasonConstants.UnconfirmedEmail
            : _options.IsConfirmedPhoneNumberRequired && !user.IsPhoneNumberConfirmed ? SignInReasonConstants.UnconfirmedPhone
            : null;

    private static SignInResult NotAllowed(IdentityUser user, string unmet)
        => new() { Status = SignInStatus.NotAllowed, UserId = IdOf(user), UnmetPrecondition = unmet };

    /// <summary>Records the attempt in the sign-in log, when registered, and returns the result carrying the row's id.</summary>
    /// <param name="result">The outcome.</param>
    /// <param name="method">The step, one of <see cref="SignInMethodConstants"/>.</param>
    /// <param name="user">The account the attempt named; null when unknown.</param>
    /// <param name="subject">The identifier as typed; stored masked.</param>
    /// <param name="reason">Why it failed; null derives it from the outcome.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<SignInResult> LogAsync(SignInResult result, string method, IdentityUser? user, string? subject, string? reason, CancellationToken cancellationToken)
    {
        if (_signInLog is null)
            return result;

        var entryId = await _signInLog.RecordAsync(
            new SignInAttempt
            {
                Method = method,
                Result = result.Status switch
                {
                    SignInStatus.Succeeded => SignInResultConstants.Succeeded,
                    SignInStatus.LockedOut => SignInResultConstants.LockedOut,
                    SignInStatus.NotAllowed => SignInResultConstants.NotAllowed,
                    SignInStatus.RequiresTwoFactor => SignInResultConstants.TwoFactorRequired,
                    SignInStatus.PasswordChangeRequired => SignInResultConstants.PasswordChangeRequired,
                    _ => SignInResultConstants.Failed,
                },
                Reason = reason ?? result.UnmetPrecondition,
                UserId = user?.Id,
                Subject = subject,
            },
            cancellationToken);
        return entryId is null ? result : result with { SignInLogEntryId = entryId };
    }

    /// <summary>Spends one hash verification on an unknown identifier so its timing matches a known one.</summary>
    private void SpendDecoyVerification(string password)
    {
        if (_hasher is null)
            return;

        var decoy = new IdentityUser();
        s_decoyHash ??= _hasher.HashPassword(decoy, Guid.NewGuid().ToString("N"));
        _hasher.VerifyHashedPassword(decoy, s_decoyHash, password);
    }

    private static string IdOf(IdentityUser user) => user.Id.ToString();
}

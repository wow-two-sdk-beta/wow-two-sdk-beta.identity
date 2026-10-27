using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Lockout;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passkeys;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core.TwoFactor;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;

/// <summary>
/// Provides sign-in orchestration: lockout → password → preconditions → second factor → principal. The result carries a
/// principal the host issues as a cookie or a JWT; the session shape stays the host's choice. Lockout and two-factor
/// steps apply when their slices are registered; a user with two-factor enabled always needs the second factor.
/// </summary>
/// <remarks>
/// The principal records how the person signed in as <see cref="AuthenticationMethodConstants.ClaimType"/> claims:
/// <c>pwd</c> for a password, <c>hwk</c> for a passkey, <c>otp</c> and <c>mfa</c> after a second factor, and whatever
/// the caller names for a sign-in completed elsewhere; a renewal records none.
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
    private static string? s_decoyHash;

    /// <summary>Create the service over the registered slices.</summary>
    /// <param name="accounts">The core account service.</param>
    /// <param name="principals">The principal factory.</param>
    /// <param name="options">Identity options carrying the sign-in preconditions.</param>
    /// <param name="passwords">The password slice; null disables password sign-in.</param>
    /// <param name="hasher">The password hasher, used to spend equal time on unknown logins.</param>
    /// <param name="lockout">The lockout slice; null skips lockout.</param>
    /// <param name="tokens">The purpose-token issuer; null omits two-factor tickets.</param>
    /// <param name="twoFactor">The two-factor slice; null disables the second-factor methods.</param>
    /// <param name="twoFactorMethods">The delivered-code methods of the two-factor slice; null leaves only the authenticator.</param>
    /// <param name="passkeys">The passkey slice; null disables passkey sign-in.</param>
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
        UserPasskeyService? passkeys = null)
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
    }

    /// <summary>Sign in with a user name (or email, when allowed) and password.</summary>
    /// <param name="login">The user name or email.</param>
    /// <param name="password">The password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="NotSupportedException">The password slice is not registered.</exception>
    public async Task<SignInResult> PasswordSignInAsync(string login, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(login);
        ArgumentNullException.ThrowIfNull(password);

        var user = await _accounts.FindByNameAsync(login, cancellationToken);
        if (user is null && _options.AllowEmailLogin && login.Contains('@', StringComparison.Ordinal))
            user = await _accounts.FindByEmailAsync(login, cancellationToken);

        if (user is not null)
            return await PasswordSignInAsync(user, password, cancellationToken);

        SpendDecoyVerification(password);
        return FailedResult;
    }

    /// <summary>Sign in <paramref name="user"/> with a password.</summary>
    /// <param name="user">The user.</param>
    /// <param name="password">The password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="NotSupportedException">The password slice is not registered.</exception>
    public async Task<SignInResult> PasswordSignInAsync(IdentityUser user, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var passwords = _passwords ?? throw new NotSupportedException("Password sign-in needs the password slice; call .AddArgon2Passwords().");

        if (_lockout?.IsLockedOut(user) == true)
            return LockedOutResult;

        if (!await passwords.CheckPasswordAsync(user, password, cancellationToken))
        {
            return _lockout is not null && await _lockout.RecordFailedAttemptAsync(user, cancellationToken)
                ? LockedOutResult
                : FailedResult;
        }

        return await CompleteFirstFactorAsync(user, PasswordMethods, cancellationToken);
    }

    /// <summary>Sign in a user already authenticated elsewhere (external login, passwordless code); lockout and preconditions still apply.</summary>
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
    public async Task<SignInResult> SignInAsync(IdentityUser user, IReadOnlyCollection<string> methods, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(methods);
        return _lockout?.IsLockedOut(user) == true
            ? LockedOutResult
            : await CompleteFirstFactorAsync(user, methods, cancellationToken);
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
        var user = await passkeys.VerifySignInAsync(state, credentialJson, cancellationToken);
        if (user is null)
            return FailedResult;
        if (_lockout?.IsLockedOut(user) == true)
            return LockedOutResult;
        if ((_options.RequireConfirmedEmail && !user.EmailConfirmed) || (_options.RequireConfirmedPhoneNumber && !user.PhoneNumberConfirmed))
            return new SignInResult { Status = SignInStatus.NotAllowed, UserId = IdOf(user) };

        return await SucceedAsync(user, PasskeyMethods, cancellationToken);
    }

    /// <summary>
    /// Renew the session of a user who already completed sign-in (refresh-token redemption): lockout and preconditions
    /// apply again, the second factor does not.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SignInResult> RenewAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (_lockout?.IsLockedOut(user) == true)
            return LockedOutResult;
        if ((_options.RequireConfirmedEmail && !user.EmailConfirmed)
            || (_options.RequireConfirmedPhoneNumber && !user.PhoneNumberConfirmed))
            return new SignInResult { Status = SignInStatus.NotAllowed, UserId = IdOf(user) };

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
        => SecondFactorSignInAsync(userId, ticket, (factor, user) => factor.VerifyAuthenticatorCodeAsync(user, code, cancellationToken), cancellationToken);

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
            : SecondFactorSignInAsync(userId, ticket, (_, user) => _twoFactorMethods.VerifyCodeAsync(user, method, code, cancellationToken), cancellationToken);
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
            async (factor, user) => (await factor.RedeemRecoveryCodeAsync(user, recoveryCode, cancellationToken)).Succeeded,
            cancellationToken);

    private async Task<SignInResult> SecondFactorSignInAsync(
        Guid userId,
        string ticket,
        Func<UserTwoFactorService, IdentityUser, Task<bool>> verify,
        CancellationToken cancellationToken)
    {
        var twoFactor = _twoFactor ?? throw new NotSupportedException("Two-factor sign-in needs the two-factor slice; call .AddTwoFactor().");
        var tokens = _tokens ?? throw new NotSupportedException("Two-factor sign-in needs purpose tokens; call .AddUserTokens(...).");

        var user = await _accounts.FindByIdAsync(userId, cancellationToken);
        if (user is null || !tokens.Verify(user, UserTokenPurposeConstants.TwoFactorSignIn, ticket))
            return FailedResult;
        if (_lockout?.IsLockedOut(user) == true)
            return LockedOutResult;

        if (!await verify(twoFactor, user))
        {
            return _lockout is not null && await _lockout.RecordFailedAttemptAsync(user, cancellationToken)
                ? LockedOutResult
                : FailedResult;
        }

        return await SucceedAsync(user, SecondFactorMethods, cancellationToken);
    }

    private async Task<SignInResult> CompleteFirstFactorAsync(IdentityUser user, IReadOnlyCollection<string> methods, CancellationToken cancellationToken)
    {
        if ((_options.RequireConfirmedEmail && !user.EmailConfirmed)
            || (_options.RequireConfirmedPhoneNumber && !user.PhoneNumberConfirmed))
            return new SignInResult { Status = SignInStatus.NotAllowed, UserId = IdOf(user) };

        if (user.TwoFactorEnabled)
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

    /// <summary>Spends one hash verification on an unknown login so its timing matches a known one.</summary>
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

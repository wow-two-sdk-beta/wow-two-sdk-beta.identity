using Microsoft.AspNetCore.Identity;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Tokens;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;

/// <summary>
/// Provides the password slice: validate, set, change, remove and check a user's password, plus token-based reset when
/// purpose tokens are registered. Every stored password rotates the security stamp, so older sessions and tokens end.
/// </summary>
/// <param name="repository">The core user store.</param>
/// <param name="accounts">The core account service, for creation with a password.</param>
/// <param name="hasher">The password hasher; Argon2id unless the host registered another.</param>
/// <param name="validators">The registered password checks, run in registration order.</param>
/// <param name="tokens">The purpose-token issuer; null until <c>.AddUserTokens()</c> registers one.</param>
/// <param name="sessions">The sessions slice, which a stored password ends at once; null without it.</param>
public sealed class UserPasswordService(
    IUserRepository repository,
    UserAccountService accounts,
    IPasswordHasher<IdentityUser> hasher,
    IEnumerable<IUserPasswordValidator> validators,
    UserTokenIssuer? tokens = null,
    UserSessionService? sessions = null)
{
    /// <summary>Whether <paramref name="user"/> has a password.</summary>
    /// <param name="user">The user to inspect.</param>
    public bool HasPassword(IdentityUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return !string.IsNullOrEmpty(user.PasswordHash);
    }

    /// <summary>Run every registered password check against <paramref name="password"/>.</summary>
    /// <param name="user">The account the password is for.</param>
    /// <param name="password">The candidate password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentityResult> ValidateAsync(IdentityUser user, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(password);

        var errors = new List<IdentityError>();
        foreach (var validator in validators)
            errors.AddRange(await validator.ValidateAsync(user, password, cancellationToken));

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]);
    }

    /// <summary>
    /// Run only the structured rules (<see cref="PasswordRulesValidator"/>) against <paramref name="password"/>: the checks
    /// that need a network, such as the breach corpus, are skipped. Sign-in uses it on a password that already matched its hash.
    /// </summary>
    /// <param name="user">The account the password belongs to.</param>
    /// <param name="password">The password as typed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentityResult> ValidateRulesAsync(IdentityUser user, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(password);

        var errors = new List<IdentityError>();
        foreach (var validator in validators.OfType<PasswordRulesValidator>())
            errors.AddRange(await validator.ValidateAsync(user, password, cancellationToken));

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]);
    }

    /// <summary>Validate the password, then create the user with it; nothing persists when the password fails.</summary>
    /// <param name="user">The new user.</param>
    /// <param name="password">The initial password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentityResult> CreateWithPasswordAsync(IdentityUser user, string password, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateAsync(user, password, cancellationToken);
        if (!validation.IsSuccess)
            return validation;

        user.PasswordHash = hasher.HashPassword(user, password);
        return await accounts.CreateAsync(user, cancellationToken);
    }

    /// <summary>Set the first password of an account that has none (for example after an external-login sign-up).</summary>
    /// <param name="user">The user.</param>
    /// <param name="password">The new password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<IdentityResult> AddPasswordAsync(IdentityUser user, string password, CancellationToken cancellationToken = default)
        => HasPassword(user)
            ? Task.FromResult(IdentityUserExtensions.Failure(IdentityErrorCodeConstants.UserAlreadyHasPassword, "The account already has a password."))
            : StoreAsync(user, password, cancellationToken);

    /// <summary>Replace the password after verifying the current one.</summary>
    /// <param name="user">The user.</param>
    /// <param name="currentPassword">The password the caller claims is current.</param>
    /// <param name="newPassword">The replacement password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<IdentityResult> ChangePasswordAsync(IdentityUser user, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
        => Verify(user, currentPassword) == PasswordVerificationResult.Failed
            ? Task.FromResult(IdentityUserExtensions.Failure(IdentityErrorCodeConstants.PasswordMismatch, "The current password is incorrect."))
            : StoreAsync(user, newPassword, cancellationToken);

    /// <summary>Remove the password, leaving only the account's other sign-in methods.</summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentityResult> RemovePasswordAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.PasswordHash = null;
        await repository.RotateSecurityStampAsync(user, sessions, cancellationToken);
        return IdentityResult.Success;
    }

    /// <summary>Whether <paramref name="password"/> matches; a match hashed with outdated parameters is re-hashed and persisted.</summary>
    /// <param name="user">The user.</param>
    /// <param name="password">The password to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>Counts no failures and ignores lockout; the sign-in slice owns both.</remarks>
    public async Task<bool> CheckPasswordAsync(IdentityUser user, string password, CancellationToken cancellationToken = default)
    {
        var result = Verify(user, password);
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password);
            await repository.UpdateAsync(user, cancellationToken);
        }

        return result != PasswordVerificationResult.Failed;
    }

    /// <summary>Issue a password-reset token; the next stored password voids it.</summary>
    /// <param name="user">The user resetting the password.</param>
    /// <exception cref="NotSupportedException">Purpose tokens are not registered.</exception>
    public string IssuePasswordResetToken(IdentityUser user)
        => RequireTokens().Issue(user, UserTokenPurposeConstants.PasswordReset);

    /// <summary>Replace the password when <paramref name="token"/> is a live reset token for <paramref name="user"/>.</summary>
    /// <param name="user">The user.</param>
    /// <param name="token">The reset token the user received.</param>
    /// <param name="newPassword">The replacement password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="NotSupportedException">Purpose tokens are not registered.</exception>
    public Task<IdentityResult> ResetPasswordAsync(IdentityUser user, string token, string newPassword, CancellationToken cancellationToken = default)
        => RequireTokens().Verify(user, UserTokenPurposeConstants.PasswordReset, token)
            ? StoreAsync(user, newPassword, cancellationToken)
            : Task.FromResult(IdentityUserExtensions.Failure(IdentityErrorCodeConstants.InvalidToken, "The reset link is invalid or has expired."));

    /// <summary>
    /// Replace the password with one the caller already ran through <see cref="ValidateAsync"/> and authorized by other proof
    /// (a spent code or link, a forced-change ticket); rotates the security stamp and ends no session, since the caller
    /// chooses which one to keep and ends the rest.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="newPassword">The replacement password, already validated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task<IdentityResult> ReplaceValidatedAsync(IdentityUser user, string newPassword, CancellationToken cancellationToken)
    {
        user.PasswordHash = hasher.HashPassword(user, newPassword);
        user.RotateSecurityStamp();
        await repository.UpdateAsync(user, cancellationToken);
        return IdentityResult.Success;
    }

    private PasswordVerificationResult Verify(IdentityUser user, string password)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(password);

        return string.IsNullOrEmpty(user.PasswordHash)
            ? PasswordVerificationResult.Failed
            : hasher.VerifyHashedPassword(user, user.PasswordHash, password);
    }

    private async Task<IdentityResult> StoreAsync(IdentityUser user, string password, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync(user, password, cancellationToken);
        if (!validation.IsSuccess)
            return validation;

        user.PasswordHash = hasher.HashPassword(user, password);
        await repository.RotateSecurityStampAsync(user, sessions, cancellationToken);
        return IdentityResult.Success;
    }

    private UserTokenIssuer RequireTokens()
        => tokens ?? throw new NotSupportedException("Password reset needs purpose tokens; call .AddUserTokens(...) on the identity builder.");
}

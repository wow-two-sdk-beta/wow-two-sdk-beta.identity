using Microsoft.Extensions.Logging;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Lockout;

/// <summary>
/// Provides the lockout slice: count failed attempts, lock for <see cref="LockoutOptions.DefaultLockout"/> once
/// <see cref="LockoutOptions.MaxFailedAttempts"/> is reached, and lock or unlock explicitly. A user with lockout
/// disabled is never counted or locked.
/// </summary>
/// <remarks>
/// A lockout refuses new credential attempts only: the account's existing sessions, refresh tokens and app grants keep
/// working through it, so failed guesses by someone else never sign the person out. Rotate the security stamp to end them.
/// </remarks>
/// <param name="repository">The core user store.</param>
/// <param name="options">Identity options carrying the lockout rules.</param>
/// <param name="timeProvider">The clock lockout windows are measured against.</param>
/// <param name="logger">Receives the lockout event.</param>
public sealed class UserLockoutService(
    IUserRepository repository,
    IdentityCoreOptions options,
    TimeProvider timeProvider,
    ILogger<UserLockoutService> logger)
{
    /// <summary>Whether <paramref name="user"/> is inside an active lockout window.</summary>
    /// <param name="user">The user.</param>
    public bool IsLockedOut(IdentityUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.IsLockedOutAt(timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Count one failed attempt; the attempt that reaches the maximum starts a lockout window. The count is a single
    /// conditional write on the stored row, so concurrent wrong attempts each count, and a parallel save of the same
    /// account never fails the request.
    /// </summary>
    /// <param name="user">The user who failed to authenticate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether this attempt locked the account.</returns>
    public async Task<bool> RecordFailedAttemptAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsLockoutEnabled)
            return false;

        var end = timeProvider.GetUtcNow() + options.Lockout.DefaultLockout;
        var isLocked = await repository.CountFailedAttemptAsync(user, options.Lockout.MaxFailedAttempts, end, cancellationToken);
        if (isLocked)
            logger.UserLockedOut(user.Id.ToString(), end);

        return isLocked;
    }

    /// <summary>Clear the failed-attempt count after a successful authentication, in place on the stored row.</summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ResetFailedAttemptsAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.AccessFailedCount == 0)
            return;

        await repository.ResetFailedAttemptsAsync(user, cancellationToken);
    }

    /// <summary>Lock the account until <paramref name="end"/>, or unlock it with null. Existing sessions stay valid.</summary>
    /// <param name="user">The user.</param>
    /// <param name="end">The instant the lockout ends; null or past unlocks.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>Rotate the security stamp as well to end the account's current sessions.</remarks>
    public async Task<IdentityResult> LockUntilAsync(IdentityUser user, DateTimeOffset? end, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsLockoutEnabled)
            return IdentityUserExtensions.Failure(IdentityErrorCodeConstants.LockoutNotEnabled, "Lockout is disabled for this account.");

        user.LockoutEnd = end;
        if (end is null)
            user.AccessFailedCount = 0;

        await repository.UpdateAsync(user, cancellationToken);
        return IdentityResult.Success;
    }

    /// <summary>Enable or disable lockout for the account; disabling also clears any window and count.</summary>
    /// <param name="user">The user.</param>
    /// <param name="isEnabled">Whether lockout applies.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SetLockoutEnabledAsync(IdentityUser user, bool isEnabled, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.IsLockoutEnabled = isEnabled;
        if (!isEnabled)
        {
            user.LockoutEnd = null;
            user.AccessFailedCount = 0;
        }

        await repository.UpdateAsync(user, cancellationToken);
    }
}

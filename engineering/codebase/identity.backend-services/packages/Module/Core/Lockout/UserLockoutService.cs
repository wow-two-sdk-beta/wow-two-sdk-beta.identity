using Microsoft.Extensions.Logging;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Lockout;

/// <summary>
/// Provides the lockout slice: count failed attempts, lock for <see cref="LockoutOptions.DefaultLockout"/> once
/// <see cref="LockoutOptions.MaxFailedAttempts"/> is reached, and lock or unlock explicitly. A user with lockout
/// disabled is never counted or locked.
/// </summary>
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

    /// <summary>Count one failed attempt; the attempt that reaches the maximum starts a lockout window.</summary>
    /// <param name="user">The user who failed to authenticate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether this attempt locked the account.</returns>
    public async Task<bool> RecordFailedAttemptAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.LockoutEnabled)
            return false;

        user.AccessFailedCount++;
        var locked = user.AccessFailedCount >= options.Lockout.MaxFailedAttempts;
        if (locked)
        {
            user.LockoutEnd = timeProvider.GetUtcNow() + options.Lockout.DefaultLockout;
            user.AccessFailedCount = 0;
            logger.UserLockedOut(user.Id.ToString(), user.LockoutEnd.Value);
        }

        await repository.UpdateAsync(user, cancellationToken);
        return locked;
    }

    /// <summary>Clear the failed-attempt count after a successful authentication.</summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ResetFailedAttemptsAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.AccessFailedCount == 0)
            return;

        user.AccessFailedCount = 0;
        await repository.UpdateAsync(user, cancellationToken);
    }

    /// <summary>Lock the account until <paramref name="end"/>, or unlock it with null. Existing sessions stay valid.</summary>
    /// <param name="user">The user.</param>
    /// <param name="end">The instant the lockout ends; null or past unlocks.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>Rotate the security stamp as well to end the account's current sessions.</remarks>
    public async Task<IdentityResult> LockUntilAsync(IdentityUser user, DateTimeOffset? end, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.LockoutEnabled)
            return IdentityUserExtensions.Failure(IdentityErrorCodeConstants.LockoutNotEnabled, "Lockout is disabled for this account.");

        user.LockoutEnd = end;
        if (end is null)
            user.AccessFailedCount = 0;

        await repository.UpdateAsync(user, cancellationToken);
        return IdentityResult.Success;
    }

    /// <summary>Enable or disable lockout for the account; disabling also clears any window and count.</summary>
    /// <param name="user">The user.</param>
    /// <param name="enabled">Whether lockout applies.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SetLockoutEnabledAsync(IdentityUser user, bool enabled, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.LockoutEnabled = enabled;
        if (!enabled)
        {
            user.LockoutEnd = null;
            user.AccessFailedCount = 0;
        }

        await repository.UpdateAsync(user, cancellationToken);
    }
}

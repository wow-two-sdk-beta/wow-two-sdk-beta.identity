# Identity.Core.Lockout

Lockout slice: count failed attempts on the user row and lock for a window once the maximum is reached.

## Quick start

```csharp
services.AddUserAccounts(o => { o.Lockout.MaxFailedAttempts = 5; o.Lockout.DefaultLockout = TimeSpan.FromMinutes(15); })
        .AddLockout();

if (lockout.IsLockedOut(user)) return Locked();
if (!await passwords.CheckPasswordAsync(user, input)) { await lockout.RecordFailedAttemptAsync(user); return Denied(); }
await lockout.ResetFailedAttemptsAsync(user);
```

## Notes

- The attempt that reaches the maximum sets `LockoutEnd = now + DefaultLockout` and resets the count. Counting is a
  conditional update of the stored row (`IUserRepository.CountFailedAttemptAsync`): parallel wrong attempts each count,
  exactly one starts the window, and the write never fails a request on the concurrency stamp.
- Users created while `IsEnabledForNewUsers` is false are never counted or locked; `SetLockoutEnabledAsync` changes it.
- `LockUntilAsync(user, end)` locks or unlocks explicitly but keeps sessions; rotate the stamp to end them.
- Automatic lockout never rotates the stamp, so an attacker guessing passwords cannot sign the owner out: a lockout
  refuses new credential attempts only, while the person's sessions, refresh tokens and app grants (`/connect/token`,
  `/connect/authorize` with a cookie, the account API's `refresh`) keep working through it.
- An account without a password is never counted: a password attempt against it answers as an unknown identifier.

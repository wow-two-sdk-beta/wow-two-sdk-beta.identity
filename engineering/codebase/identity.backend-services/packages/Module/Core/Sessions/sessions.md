# Identity.Core.Sessions

Sign-in sessions (I16): one `identity_sessions` row per signed-in browser or device, its id the `sid` claim of the
cookie and of every OIDC token issued under it. A person lists their live sessions and revokes one or all; revoking
ends the cookie at its next request, the OIDC grants issued under the session and their refresh chains.

## Quick start

```csharp
identity.AddSecurityStampValidation().AddSessions().AddSignIn().AddAccountEndpoints();
services.AddCookieAuthentication();

app.MapGroup("/account").MapUserAccountEndpoints();      // login?useCookies=true starts a session; manage/sessions
```

```jsonc
"Identity": {
  "Sessions": {
    "Lifetime": "12:00:00",              // absolute from sign-in; the cookie expires with it
    "ActivityInterval": "00:05:00"       // how stale last_seen_at may grow before a request moves it
  }
}
```

| Route | Does |
|---|---|
| `GET manage/sessions` | the live sessions, newest first: `id`, `createdAt`, `lastSeenAt`, `expiresAt`, `authenticationMethods`, `ipMasked`, `userAgent`, `current` |
| `DELETE manage/sessions/{id}` | revokes one; `404` when it is not the caller's or already ended; revoking the caller's own signs it out |
| `DELETE manage/sessions[?keepCurrent=true]` | revokes all, or all but the caller's |

## Lifecycle

- Start: the account API's cookie sign-in calls `UserSessionService.StartAsync` with the principal's `amr` values,
  adds the `sid` claim (`IdentityCoreOptions.Claims.SessionIdClaimType`) and sets the cookie's expiry to the session's.
  Bearer sign-ins start none: their refresh tokens are the session.
- Validate: every cookie request that carries a `sid` reads its row (`ValidateAsync`), so a revocation on any replica
  ends the cookie at once; a request moves `last_seen_at` at most once per `ActivityInterval`.
- Live means unrevoked, unexpired and stamped with the person's current security stamp. A stamp rotation (password
  change, sign-out everywhere) ends every session; the row is marked `security_stamp` when next seen.
- End: `RevokeAsync` (one, reason `revoked`; `sign_out` from the logout and end-session routes) and `RevokeAllAsync`
  (reason `revoked_all`, optionally keeping one) mark the rows, then run every `ISessionRevocationHandler`.

## Types

| Type | Role |
|---|---|
| `IdentitySession` | the row: person, `amr` values, stamp, created, last seen, expires, revoked and why, masked network, user agent |
| `UserSessionService` | start, validate, list, revoke one, revoke all |
| `IUserSessionRepository` · `EfUserSessionRepository` | the store over `IdentityDbContext`; touches and revocations are conditional updates |
| `ISessionRevocationHandler` | what else ends with a session; the OIDC server's revokes the session's authorizations and tokens |
| `SessionRevokeReasonConstants` | `sign_out`, `revoked`, `revoked_all`, `security_stamp` |

## Notes

- A cookie without `sid` (issued before the slice, or not by the account API) passes the session check; the stamp
  check still covers it.
- The address is kept as its network only (IPv4 /24, IPv6 /48) and the user agent to 256 characters.
- Rows outlive their session for the person's history and the sign-in log's `session_id`; a prune job waits.
- Account deletion removes the person's sessions with the rest of the account.

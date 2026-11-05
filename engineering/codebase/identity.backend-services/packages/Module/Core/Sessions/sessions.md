# Identity.Core.Sessions

Sign-in sessions (I16): one `identity_sessions` row per signed-in browser or device, its id the `sid` claim of the
cookie and of every OIDC token issued under it. A person lists their live sessions and revokes one or all; revoking
ends the cookie at its next request, the OIDC grants issued under the session and their refresh chains.

## Quick start

```csharp
identity.AddSecurityStampValidation().AddSessions().AddSignIn().AddAccountEndpoints();
services.AddCookieAuthentication();

app.MapGroup("/api/account").MapUserAccountEndpoints();  // sign-in?useCookies=true starts a session; manage/sessions
```

```jsonc
"Identity": {
  "Sessions": {
    "Expiration": "Fixed",               // Fixed: an absolute end from sign-in; Sliding: an end each use moves
    "Lifetime": "12:00:00",              // fixed, by default: the cookie ends with it and with the browser
    "ExtendedLifetime": "30.00:00:00",   // fixed, when the sign-in asks to stay signed in; empty offers no such choice
    "SlidingLifetime": "7.00:00:00",     // sliding: how long a session lives after its last use (Ocharo: 5 days)
    "ActivityInterval": "00:05:00"       // how stale last_seen_at may grow before a request moves it (and a sliding end)
  }
}
```

| `Expiration` | A sign-in's session ends | Its cookie | "Keep me signed in" |
|---|---|---|---|
| `Fixed` | `Lifetime` after sign-in | ends with the browser too | offered while `ExtendedLifetime` is set: the session then ends `ExtendedLifetime` after sign-in, with a lasting cookie |
| `Sliding` | `SlidingLifetime` after its last use | lasting, reissued as the end moves | not offered: a used session stays signed in |

- The end covers everything issued under the session: the cookie, every app's OIDC refresh chain (each token response
  names it as `refresh_token_expires_in`) and so each BFF session, and a bearer sign-in's refresh family.
- A sliding session has no absolute end: it lives while used at least once per `SlidingLifetime`, until revoked, signed
  out or ended by a stamp rotation.

| Route | Does |
|---|---|
| `GET manage/sessions` | the live sessions, newest first: `id`, `createdAt`, `lastSeenAt`, `expiresAt`, `authenticationMethods`, `ipMasked`, `userAgent`, `current`, `trustedContactDisplayName` |
| `DELETE manage/sessions/{id}` | revokes one; `404` when it is not the caller's or already ended; revoking the caller's own signs it out |
| `DELETE manage/sessions[?keepCurrent=true]` | revokes all, or all but the caller's |

## Lifecycle

- Start: the account API's cookie sign-in calls `UserSessionService.StartAsync` with the principal's `amr` values and
  the request's `isPersistent`, adds the `sid` claim (`IdentityCoreOptions.Claims.SessionIdClaimType`) and sets the
  cookie's expiry and persistence to the session's. Bearer sign-ins start none: their refresh family is the session,
  ending by the same settings.
- Validate: every cookie request that carries a `sid` reads its row (`FindLiveAsync`), so a revocation on any replica
  ends the cookie at once; a request moves `last_seen_at` at most once per `ActivityInterval`, and with it a sliding
  session's end, reissuing the cookie. The OIDC token endpoint validates the same way, so an app's refresh uses the
  session too.
- Live means unrevoked, unexpired and stamped with the person's current security stamp. A stamp rotation (password
  change, sign-out everywhere) ends every session at once, marks the rows `security_stamp` and runs every
  `ISessionRevocationHandler`; a rotation that kept its own session rebinds it first.
- A role change (`../Roles/roles.md` § Role changes) ends no session; `roles_changed` stays only on rows from before.
- End: `RevokeAsync` (one, reason `revoked`; `sign_out` from the sign-out and end-session routes) and `RevokeAllAsync`
  (reason `revoked_all`, or the one given, optionally keeping one) mark the rows, then run every `ISessionRevocationHandler`.

## Types

| Type | Role |
|---|---|
| `IdentitySession` | the row: person, `amr` values, stamp, created, last seen, expires, revoked and why, masked network, user agent |
| `UserSessionService` | start, validate, list, revoke one, revoke all |
| `IUserSessionRepository` · `EfUserSessionRepository` | the store over `IdentityDbContext`; touches and revocations are conditional updates |
| `ISessionRevocationHandler` | what else ends with a session; the OIDC server's revokes the session's authorizations and tokens |
| `SessionRevokeReasonConstants` | `sign_out`, `revoked`, `revoked_all`, `account_deleted`, `security_stamp`, `roles_changed`, `trusted_contact_revoked` |

## Notes

- A cookie without `sid` (issued before the slice, or not by the account API) passes the session check; the stamp
  check still covers it.
- The address is kept as its network only (IPv4 /24, IPv6 /48) and the user agent to 256 characters.
- Rows outlive their session for the person's history and the sign-in log's `session_id`; a prune job waits.
- Account deletion removes the person's sessions with the rest of the account.
- A sign-in through a trusted contact names it in `trusted_contact_user_id` (`010-trusted-contacts`); revoking the contact
  ends those sessions (`RevokeThroughContactAsync`, reason `trusted_contact_revoked`). See `../TrustedContacts/trusted-contacts.md`.

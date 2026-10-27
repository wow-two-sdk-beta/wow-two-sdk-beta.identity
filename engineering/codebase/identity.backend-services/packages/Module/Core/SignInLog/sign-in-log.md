# Identity.Core.SignInLog

The sign-in log (I16): every sign-in attempt, successful or not, in `identity_sign_in_log`, kept for a configured
retention. It answers "who tried to sign in as whom, how, from where, and how did it end".

## Quick start

```csharp
identity.AddArgon2Passwords().AddLockout().AddSignIn().AddSignInLog(o => o.RetentionDays = 180);
```

```jsonc
"Identity": {
  "SignInLog": {
    "RetentionDays": 180,                // rows older than this are deleted
    "CleanupInterval": "01:00:00"        // the cleanup runs at startup, then this often
  }
}
```

## What a row holds

| Column | Holds |
|---|---|
| `occurred_at` | when (UTC) |
| `user_id` | the account the attempt named; null for an unknown login |
| `subject_masked` | the typed login as a hint: `a***@example.com`, `+998*******45`, `al***` |
| `method` | `password`, `passkey`, `email_link`, `email_code`, `external`, or `2fa:{method}` (`2fa:authenticator`, `2fa:sms`, `2fa:recovery_code`) |
| `client_id` | the OIDC client the sign-in serves, when the hosted pages pass `?clientId=` |
| `result` · `reason` | `succeeded`, `failed`, `locked_out`, `not_allowed`, `two_factor_required` · `unknown_account`, `wrong_password`, `wrong_code`, `invalid_passkey`, `invalid_ticket`, `invalid_link_or_code`, `locked_out`, `lockout_started`, `unconfirmed_email`, `unconfirmed_phone` |
| `ip_masked` · `user_agent` | the network (IPv4 /24, IPv6 /48) · the user agent, cut to 256 characters |
| `session_id` | the session a successful cookie sign-in started |
| `trace_id` | the request's W3C trace id |

## Notes

- `SignInService` records every attempt it judges, whatever the outcome, and returns the row's id
  (`SignInResult.SignInLogEntryId`); the account API records the ones it refuses first (a spent email link, a missing
  two-factor ticket) and ties a cookie sign-in's row to its session.
- A two-factor sign-in is two rows: the first factor (`two_factor_required`), then the second (`2fa:…`).
- A failed write is logged and swallowed: the log never blocks a sign-in.
- The cleanup is one `DELETE … WHERE occurred_at < cut-off`, idempotent, so every replica may run it; no lock needed.
- A plain table for now; monthly partitions (T10 row 21) wait until the volume asks for them. `country` waits for a
  geo source, `organization_id` for a sign-in that picks one.
- Account deletion leaves the rows to their retention; the personal-data export lists them.

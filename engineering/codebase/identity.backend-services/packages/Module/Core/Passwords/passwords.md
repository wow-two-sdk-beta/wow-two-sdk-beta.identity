# Identity.Core.Passwords

Password slice over the core user row: rules, optional breach check, set / change / remove / check, token reset, and the
confirmed password change (none, a code or a link).
Every stored password rotates the security stamp, ending older sessions and purpose tokens.

## Quick start

```csharp
services.AddUserAccounts(o => o.Password.MinLength = 12)
        .AddArgon2Passwords()               // rules + Argon2id (a hasher registered earlier wins)
        .AddBreachedPasswordCheck()         // optional: Pwned Passwords k-anonymity range query
        .AddUserTokens(o => o.SigningKey = cfg["Identity:TokenKey"])   // enables reset and the forced-change ticket
        .AddPasswordChange();               // optional: the confirmed change and the change a sign-in forces

var result = await passwords.CreateWithPasswordAsync(user, "correct horse battery");
var ok     = await passwords.CheckPasswordAsync(user, input);            // upgrades outdated hashes
var token  = passwords.IssuePasswordResetToken(user);                     // mail it
await passwords.ResetPasswordAsync(user, token, newPassword);
```

```jsonc
"Identity": { "Password": {                  // applied after the code, so an environment changes the rules
  "MinLength": 15, "MaxLength": 128,
  "IsDigitRequired": false, "IsLowercaseRequired": false, "IsUppercaseRequired": false, "IsNonAlphanumericRequired": false,
  "RequiredUniqueChars": 1, "IsAccountIdentifierRejected": true,
  "Change": { "Confirmation": "None", "IsSignInAgainRequired": false, "Lifetime": "00:15:00", "LinkTemplate": null }
} }
```

## Notes

- Rules follow NIST SP 800-63B: `MinLength` 15, `MaxLength` 128, composition rules off, account identifiers rejected.
- `Identity:Password` binds over the code's `AddUserAccounts(o => o.Password…)`; a value invalid on its own refuses the start.
- Validators run in registration order and report every failure; add one by registering `IUserPasswordValidator`.
- The breach check sends only five SHA-1 hex characters, asks for padded responses and fails open by default
  (`IsFailOpen = false` rejects the password when the corpus is unreachable).
- `CheckPasswordAsync` counts no failures; lockout belongs to the sign-in slice.
- Argon2id verifies with the cost recorded in each hash and re-hashes a match recorded with other parameters.
- `ValidateRulesAsync` runs only the structured rules (`PasswordRulesValidator`), never the breach check; sign-in uses it.

## Password change

`UserPasswordChangeService` (`.AddPasswordChange()`, needs `.AddArgon2Passwords()` and `.AddUserTokens(…)`) changes the
password of a person who is signed in, or who holds the ticket a sign-in answered with.

| `Identity:Password:Change:Confirmation` | What the person does |
|---|---|
| `None` (default) | enters the current password and the new one |
| `Code` | asks for a code (`SendConfirmationAsync`), then enters the current password, the code and the new password |
| `Link` | asks for a link; only the link's page changes the password, with no current password (`ChangeByLinkAsync`) |

- A code or link is a one-use `IOtpService` code (scope `password-change`, `Lifetime` 15 minutes by default) sent to the
  first verified channel that has a delivery handler; an `IUserVerifiedChannelSource` lists a person's verified channels
  (`.AddEmailConfirmation()` lists a confirmed email).
- A link carries `{userId}.{code}` in the URL fragment (`LinkTemplate`, `{token}` in it, required for `Link`); a host with an
  issuer origin fills a missing template with its own `/change-password#token={token}` page.
- The new password passes the rules before the code is spent, so a weak password leaves the code usable.
- A change ends every other session at once and keeps the one it was made in, re-bound to the new stamp, unless
  `IsSignInAgainRequired` is `true`; a link change ends them all.
- No verified channel answers `NoVerifiedChannel`; a code or link sent moments ago answers `PasswordChangeRateLimited`.
- Raising a rule (a longer `MinLength`) forces a change at sign-in: `SignInService` checks the typed password of every
  match against the structured rules, answers `PasswordChangeRequired` with a ticket (purpose token, 15 minutes, bound to
  the person and stamp) and grants no session. The ticket opens the change routes only, replaces the current password and
  dies with the change; the old hash stays until then. `Link` still demands the link.

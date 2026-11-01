# Identity.Core.Endpoints

The account HTTP API over the identity slices — the SDK's `MapIdentityApi`, built on our own user model.

## Quick start

```csharp
services.AddUserAccounts()
        .AddArgon2Passwords().AddUserTokens(o => o.SigningKey = key).AddEmailConfirmation()
        .AddLockout().AddRefreshTokens().AddPasswordChange().AddSignIn()
        .AddAccountEndpoints(o => o.IsEnabled = true);       // email links + texts from configuration; sign-up on
services.AddJwtTokenIssuance(…).AddJwtBearerAuthentication(…);

app.MapGroup("/account").MapUserAccountEndpoints();
```

```jsonc
"UserAccounts": { "Emails": {
  "ConfirmationLink": "https://app.example/confirm-email?userId={userId}&token={token}",
  "ResetLink": "https://app.example/reset-password?email={email}&token={token}"
} },
"Identity": { "SignUp": { "IsEnabled": true } }  // wins over code; read live
```

| Route | Does |
|---|---|
| `POST register` | creates the account and sends the confirmation email when configured, only while sign-up is on |
| `POST login[?useCookies=true]` | bearer `{ accessToken, expiresIn, refreshToken }` or a cookie; `twoFactorCode` / `recoveryCode` / `twoFactorMethod` in the body; `200` with `{ isPasswordChangeRequired, confirmation, ticket }` and no session when the password breaks the current rules |
| `POST refresh` · `POST logout` | rotate the refresh token · revoke it, end the cookie's session and clear the cookie |
| `GET confirm-email?userId&token` | confirms the email |
| `POST resend-confirmation-email` · `POST forgot-password` | always `204`, so they reveal no accounts |
| `POST reset-password` | sets a new password with the emailed token |
| `POST manage/password/confirmation` · `POST manage/password` | password change: ask how it is confirmed (sends a code or link), then change it; a signed-in person or a `ticket` holder; mapped only with `.AddPasswordChange()` |
| `POST password/change` | changes the password with the token of an emailed link, anonymous; mapped with `.AddPasswordChange()` |
| `GET manage/info` | the signed-in account (authorized) |
| `GET manage/2fa` · `POST manage/2fa/authenticator` · `enable` · `disable` · `recovery-codes` | two-factor setup; mapped only with `.AddTwoFactor()` |
| `POST manage/2fa/methods/{method}/send` · `…/{method}/enable` · `manage/2fa/preferred` | delivered-code methods: send, enable with the code, choose the first method |
| `POST email-sign-in/send` · `POST email-sign-in[?useCookies=true]` | passwordless link or code; mapped only with `.AddEmailSignIn()` |
| `POST login/two-factor[?useCookies=true]` | finishes a first factor that answered with a ticket; mapped with `.AddTwoFactor()` |
| `GET manage/personal-data` · `POST manage/delete-account` | export and erasure; mapped only with `.AddPersonalData()` |
| `POST passkeys/login/options` · `POST passkeys/login[?useCookies=true]` | usernameless passkey sign-in; mapped only with `.AddPasskeys()` |
| `GET manage/passkeys` · `POST manage/passkeys/options` · `POST manage/passkeys` · `DELETE manage/passkeys/{id}` | list, start, finish and remove registrations |
| `GET manage/sessions` · `DELETE manage/sessions/{id}` · `DELETE manage/sessions[?keepCurrent=true]` | list, revoke one, revoke all sign-in sessions; mapped only with `.AddSessions()` (`../Sessions/sessions.md`) |

- Success bodies use the `ApiResponse<T>` envelope (`{ data }`); failures are problem details with identity codes,
  translated when error translation is enabled.
- With `.AddSessions()`, a cookie sign-in starts a session: the cookie carries its `sid` and expires with it.
- Every sign-in route takes an optional `?clientId=` (the OIDC client the hosted pages serve), which the sign-in log
  records with each attempt (`.AddSignInLog()`, `../SignInLog/sign-in-log.md`).
- Lockout answers `429`, an unconfirmed account `403`, wrong credentials and a missing second factor `401`.
- Sign-up is off by default: `register` answers `403` (message key `SignUpClosed`) and creates nothing until
  `SignUpOptions.IsEnabled` turns it on, in code or in the host section `Identity:SignUp`; an admin's code path
  (`UserAccountService`) still creates accounts, and sign-in is unaffected.
- Refresh renews without a second factor but re-checks lockout and confirmation; a refused renewal revokes the token.
- Without an `IEmailBroker` or a link template, emails are skipped and `isConfirmationSent` is false.
- A host with no JWT issuance (`ITokenIssuer`) or refresh slice is cookie-only: a bearer sign-in (any sign-in route without
  `?useCookies=true`) and `refresh` answer `400` (`BearerSignInUnsupported`) before any credential is checked.
- Failures that carry their own code put it in the problem's `identityCode` extension, beside the message key translation uses.

## Password change

- `manage/password/confirmation` takes `{ ticket? }` and answers `{ confirmation, isSent }` (`None`, `Code` or `Link`);
  `None` sends nothing. `429` `PasswordChangeRateLimited`, `422` `NoVerifiedChannel`, `503` `PasswordChangeDeliveryFailed`.
- `manage/password` takes `{ currentPassword, newPassword, code?, ticket? }` and answers `204`. The ticket replaces the
  current password; without a ticket the person must be signed in (`401` otherwise). `Link` confirmation answers `403`
  `PasswordChangeLinkRequired`; a wrong or spent code is `InvalidPasswordChangeCode`, a weak password the rule codes.
- `password/change` takes `{ token, newPassword }` (the link's `{userId}.{code}` token) and answers `204`;
  `InvalidPasswordChangeLink` for a wrong, spent or expired one.
- A change from a cookie session reissues the cookie with the new stamp and the same `sid`, or clears it when
  `IsSignInAgainRequired`; every other session ends. Rules and confirmation modes: `../Passwords/passwords.md`.
- A login whose password breaks the current rules answers `200` with `Cache-Control: no-store`, never a session; after
  a second factor it answers the same (`FinishPasswordSignInAsync`).

## Sign-in by phone

`MapPhoneSignInEndpoints` (not part of `MapUserAccountEndpoints`) adds `POST phone-sign-in/send` and
`POST phone-sign-in[?useCookies=true]` to the same group: `../PhoneSignIn/phone-sign-in.md`.

## Hosted page links

- `UserAccounts:Emails:{ConfirmationLink,ResetLink}` are the templates the emails carry (`{userId}`, `{email}`, `{token}`).
  `Identity.Api` fills a missing one from the origin of `Identity:Server:Issuer`: `/confirm-email?userId=…&token=…`,
  `/reset-password?email=…&token=…`, and the change page `/change-password#token=…`; with no issuer there is no link.

## Two-factor login

- A login that needs the second factor answers `401` with `twoFactorMethod`, `twoFactorMethods` and, for a delivered
  method, `isCodeSent` / `codeExpiresAt` as problem extensions; `IsCodeSentAutomatically` sends the preferred method's code then.
- Resend or switch: repeat the login with `twoFactorMethod` and no code. Complete: repeat it with `twoFactorCode`.

## Passkeys

- `options` holds WebAuthn JSON for `PublicKeyCredential.parseCreationOptionsFromJSON` / `parseRequestOptionsFromJSON`.
- Send back `{ state, credential: credential.toJSON() }`; registration also takes an optional `name`.
- A failed or replayed passkey login answers `401`; lockout `429` and unconfirmed `403`, as for passwords.

# Identity.Core.SignIn

`SignInService` orchestrates lockout → password → preconditions → second factor → principal. The result carries a
`ClaimsPrincipal`; the host chooses the session shape.

## Quick start

```csharp
identity.AddArgon2Passwords().AddLockout().AddUserTokens(o => o.SigningKey = key).AddTwoFactor()
        .AddPasswordChange()                                       // optional: forces a change when a stored password breaks the rules
        .AddSignIn();

var result = await signIn.PasswordSignInAsync(request.Identifier, request.Password);
switch (result.Status)
{
    case SignInStatus.Succeeded:
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, result.Principal!);   // cookie
        // or: tokenIssuer.Issue(result.Principal!.Claims)                                             // JWT
        break;
    case SignInStatus.RequiresTwoFactor:
        return new { result.UserId, result.TwoFactorTicket };      // client returns both with the code
    case SignInStatus.PasswordChangeRequired:
        return new { result.PasswordChangeTicket };                // no session: the ticket opens the change routes only
}

await signIn.TwoFactorSignInAsync(userId, ticket, code);          // or RecoveryCodeSignInAsync
```

## Notes

- Unknown identifiers spend one hash verification, so response time does not reveal which accounts exist; an account
  without a password (a phone or passkey person) answers the same way and counts no failed attempt (log reason `no_password`).
- Wrong passwords and wrong second factors both count toward lockout; the failure count resets only on success.
- `RenewAsync` (refresh) re-checks the preconditions but not a lockout, which guards new credential attempts only.
- A `NotAllowed` result names its precondition in `UnmetPrecondition` (`unconfirmed_email`, `unconfirmed_phone`); an
  account with no email but a confirmed phone meets `IsConfirmedEmailRequired` through its phone.
- A user with `IsTwoFactorEnabled` always gets `RequiresTwoFactor`, even when the two-factor slice is absent.
- The two-factor ticket is a purpose token (5 minutes by default) bound to the user and stamp.
- `SignInAsync(user)` completes external-account and passwordless flows with the same lockout and precondition checks;
  `SignInAsync(user, methods)` also records how (an emailed code passes `otp`).
- The principal carries `amr` claims (`AuthenticationMethodConstants`): `pwd` after a password, `hwk` after a passkey,
  `otp` and `mfa` after a second factor; a renewal records none. The OIDC server copies them into tokens.
- `PasskeySignInAsync(state, assertionJson)` verifies a passkey, then applies lockout and preconditions; no second factor.
- Preconditions: `IdentityCoreOptions.SignIn` (`IsConfirmedEmailRequired`, `IsConfirmedPhoneNumberRequired`, `IsEmailIdentifierAllowed`),
  set in code or in the host section `Identity:SignIn`, which is applied last.
- With `.AddPasswordChange()`, a password that matches its hash is then checked against the current structured rules (never
  the breach check); one that fails them ends as `PasswordChangeRequired` with `PasswordChangeTicket`, no principal, and
  the sign-in log reason `PasswordBreaksRules`. The old hash stays until the person changes it (`../Passwords/passwords.md`).
- A person with a second factor who also needs the change gets `RequiresTwoFactor` with `IsPasswordChangeRequired` set;
  `FinishPasswordSignInAsync(firstFactor, completed)` turns the completed second factor into `PasswordChangeRequired`.
- With `.AddSignInLog()`, every attempt it judges leaves a sign-in log row (`../SignInLog/sign-in-log.md`) and the result
  carries its id (`SignInLogEntryId`); `SignInAsync(user, methods, logMethod)` names the step the row records.
- A cookie sign-in through the account API starts a session (`../Sessions/sessions.md`); the principal then carries `sid`.

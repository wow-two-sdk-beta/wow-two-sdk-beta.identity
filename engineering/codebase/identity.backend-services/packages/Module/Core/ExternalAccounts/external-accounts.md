# Identity.Core.ExternalAccounts

External accounts: link a provider account (`Provider` + `ProviderKey`) to a user and find the user behind it.
The OAuth handshake stays with `Identity/OAuth/*`; this slice only persists the link. The rows live in
`identity_user_logins`, whose provider column keeps its `001` name `login_provider`.

## Quick start

```csharp
identity.AddExternalAccounts().AddSignIn();

var user = await externalAccounts.FindByExternalAccountAsync("Google", subject)
    ?? await CreateAndLinkAsync(subject);                          // host decides sign-up policy
var result = await signIn.SignInAsync(user);
```

## Notes

- A provider account links to one user; linking it elsewhere fails with `ExternalAccountAlreadyLinked`.
- Unlinking rotates the security stamp.

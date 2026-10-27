# WoW2.Sdk.Beta.Be.Identity.Module

Form 1 of Identity: user accounts in the consumer's process, over the consumer's database.

- `AddUserAccounts()` and its slices: passwords, tokens, email, lockout, security stamps, roles, claims, logins,
  two-factor, phones, refresh tokens, passkeys, organizations, email sign-in, personal data, the account endpoints
- its own `IdentityDbContext` over 11 `identity_*` account tables and OpenIddict's four `identity_oidc_*` tables (the
  Identity service's OIDC server uses them), created by embedded SQL (PostgreSQL and SQLite) that runs as the be sdk
  migration set `identity`; uuid v7 keys, no generic entities
- OTP with email, SMS, Telegram, Telegram Gateway and WhatsApp channels; TOTP; WebAuthn; Argon2 hashing
- the ru/uz texts of the identity error codes, contributed to error translation by `AddUserAccounts`

```bash
dotnet add package WoW2.Sdk.Beta.Be.Identity.Module --prerelease
```

```csharp
services.AddUserAccounts();                                  // Identity:Database: Provider, Schema, ConnectionStringName
await app.Services.MigrateBespokeOnStartupAsync();           // a local boot applies the set; a deployed boot verifies it
```

Namespaces are unchanged from the be sdk: `WoW.Two.Sdk.Beta.Be.Identity.{Core,Otp,Mfa,PasswordHashing}`.

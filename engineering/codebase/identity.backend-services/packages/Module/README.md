# WoW2.Sdk.Beta.Be.Identity.Module

Form 1 of Identity: user accounts in the consumer's process, over the consumer's database.

- `AddUserAccounts<TUser>()` and its slices: passwords, tokens, email, lockout, security stamps, roles, claims, logins,
  two-factor, phones, refresh tokens, passkeys, organizations, email sign-in, personal data, the account endpoints
- `ApplyIdentitySchema` on the consumer's `DbContext` (EF-owned schema, 11 tables)
- OTP with email, SMS, Telegram, Telegram Gateway and WhatsApp channels; TOTP; WebAuthn; Argon2 hashing
- the ru/uz texts of the identity error codes, contributed to error translation by `AddUserAccounts`

```bash
dotnet add package WoW2.Sdk.Beta.Be.Identity.Module --prerelease
```

```csharp
modelBuilder.ApplyIdentitySchema<IdentityUser, IdentityRole, Guid>();          // in the consumer's DbContext
services.AddUserAccounts<IdentityUser>().AddEntityFrameworkStores<AppDbContext>();
```

Namespaces are unchanged from the be sdk: `WoW.Two.Sdk.Beta.Be.Identity.{Core,Otp,Mfa,PasswordHashing}`.

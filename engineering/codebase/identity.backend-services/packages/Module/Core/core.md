# Identity.Core

Our **own** identity system (not a wrapper over `Microsoft.AspNetCore.Identity`), exposed as orthogonal lego slices —
compose only what an app needs. Core is the mandatory slice: the user entity, the Module's own database, the
normalizer and the `UserAccountService` facade. Entities carry ASP.NET-Identity-shaped columns, keyed by uuid v7.

> Build order: `engineering/research/identity/identity-architecture.md` §9; slice track: `identity-slices.md`.
> Every slice below is opt-in through the builder `AddUserAccounts` returns.

## Quick start

```csharp
// 1. register core: the facade, IdentityDbContext and the `identity` migration set
services.AddUserAccounts(o => o.User.RequireUniqueEmail = true);

// 2. apply the schema beside the host's own sets: at a local boot, or in the deploy's migrate step
await app.Services.MigrateBespokeOnStartupAsync(app.Lifetime.ApplicationStopping);

// 3. use the facade
var service = sp.GetRequiredService<UserAccountService>();
var result  = await service.CreateAsync(new IdentityUser { UserName = "alice", Email = "alice@x.io" });   // id: uuid v7
var alice   = await service.FindByNameAsync("ALICE");   // case-insensitive via the normalizer
```

```jsonc
"Identity": {
  "Database": {
    "Schema": "identity",                // PostgreSQL schema, one per instance
    "ConnectionStringName": null         // a ConnectionStrings name; null uses the host's database
  }
}
```

## Schema

- PostgreSQL only: the SQLite dialect retired on 2026-10-27, as products may not run SQLite
  (`wow-two-ws/conventions/development/backend/dotnet/core/mla/domains/persistence/database/database.md`).
- `IdentityDbContext` maps 11 account tables: `identity_users`, `identity_roles`, `identity_user_roles`,
  `identity_user_claims`, `identity_role_claims`, `identity_user_logins`, `identity_user_tokens`,
  `identity_refresh_tokens`, `identity_passkeys`, `identity_organizations`, `identity_memberships`. Products never map
  them into their own context.
- It also maps OpenIddict's four OIDC tables, created by `002-oidc`: `identity_oidc_applications` (clients, hashed
  secrets, URIs, permissions), `identity_oidc_authorizations` (a subject's grant to a client), `identity_oidc_scopes`
  (custom scopes, empty today) and `identity_oidc_tokens` (codes, refresh and other tokens with their status). The
  entities (`IdentityOidcApplication`, `…Authorization`, `…Scope`, `…Token`) subclass OpenIddict's dependency-free
  `OpenIddict.EntityFrameworkCore.Models` with uuid v7 ids made in code; `subject` holds the user id as text. Every
  form carries the tables, so the schema has one shape; only the Server package (the Identity service) reads or writes
  them, through OpenIddict's EF Core stores.
- `003-spine` adds `identity_sessions` (one row per signed-in browser or device, its id the `sid`; `Sessions/`),
  `identity_sign_in_log` (every attempt, deleted after the retention; `SignInLog/`) and
  `identity_data_protection_keys` (the instance's Data Protection ring, read and written only by the Server package's
  `AddDataProtectionRing`).
- The SQL owns the schema: `../Migrations/Postgres/NNN-name/{Apply,Rollback}.sql`, embedded and run as the be sdk
  migration set `identity` (history `identity_migration_history`, its own advisory lock). EF only maps.
- Names are pinned in the model, whatever the host's naming convention: snake_case columns, `pk_{table}`,
  `ix_{table}_{columns}`. Scripts qualify every table with `{{schema}}`.
- The set claims its schema with an owner kind: `module` (`IdentitySchemaOwnerConstants.Module`, the default) for a
  Form 1 product, `service` for the Identity service (`AddUserAccounts(configureDatabase: o => o.SchemaOwner = …)`). A
  schema one kind claimed refuses the other's migrations.
- Connection: `Identity:Database:ConnectionStringName`, else the host's `DatabaseSettings`, else `DB_CONNECTION`, else
  `DatabaseSettings:ConnectionString`. PostgreSQL gets one pool keyed `identity`, shared by the context and the set.
- Ids: users, roles, passkeys, refresh tokens, organizations and sessions take `Guid.CreateVersion7` from the
  registered `TimeProvider` at creation; claim and sign-in log rows take a `bigint` identity, ring keys an `integer` one. No `TUser` / `TKey` generics: extend through side
  tables keyed by the user id (`sub`) and claims contributors, never by added columns.
- A schema change is a new `NNN-name` pair plus the model change; `Identity.Tests.Migrations` fails when the applied
  schema and the model disagree. Never edit an applied pair (`001` and `002` still say SQLite shares their names; they
  stay as applied).

## Slices

| Builder call | Service | Folder |
|---|---|---|
| `AddUserAccounts()` | `UserAccountService`, `UserClaimsPrincipalFactory`, `IdentityDbContext` | `./` |
| `AddArgon2Passwords()` · `AddBreachedPasswordCheck()` | `UserPasswordService` | `Passwords/` |
| `AddUserTokens(o => o.SigningKey = …)` | `UserTokenIssuer` | `Tokens/` |
| `AddEmailConfirmation()` | `UserEmailService` | `Emails/` |
| `AddLockout()` | `UserLockoutService` | `Lockout/` |
| `AddSecurityStampValidation()` | `SecurityStampValidator` + cookie/JWT hooks | `SecurityStamps/` |
| `AddRoles()` | `RoleService`, `UserRoleService` | `Roles/` |
| `AddUserClaims()` | `UserClaimService` | `UserClaims/` |
| `AddExternalLogins()` | `UserLoginService` | `Logins/` |
| `AddTwoFactor()` | `UserTwoFactorService` | `TwoFactor/` |
| `AddPhoneNumbers()` | `UserPhoneService` | `Phones/` |
| `AddRefreshTokens()` | `RefreshTokenService` | `RefreshTokens/` |
| `AddSessions(o => …)` | `UserSessionService` (one session per cookie sign-in, `sid`, list and revoke) + cookie hook | `Sessions/` |
| `AddSignInLog(o => …)` | `SignInLogService` (every attempt, masked) + the retention cleanup job | `SignInLog/` |
| `AddPasskeys(o => …)` | `UserPasskeyService`, `SignInService.PasskeySignInAsync` | `Passkeys/` |
| `AddOrganizations(o => …)` | `OrganizationService` (organizations, memberships, role and permission claims) | `Organizations/` |
| `AddEmailSignIn(o => …)` | `UserEmailSignInService` (single-use link or code over OTP) | `EmailSignIn/` |
| `AddPersonalData()` | `UserPersonalDataService` (export sections, account erasure hooks) | `PersonalData/` |
| `AddAccountEndpoints()` + `MapUserAccountEndpoints()` | account HTTP API | `Endpoints/` |
| `AddSignIn()` | `SignInService` | `SignIn/` |

Table-backed slices (`AddRoles`, `AddUserClaims`, `AddExternalLogins`, `AddTwoFactor`, `AddRefreshTokens`, `AddPasskeys`,
`AddOrganizations`, `AddSessions`, `AddSignInLog`) register their repositories over `IdentityDbContext` with `TryAdd`, so
a repository the host registered first wins.

## Notes

- Uniqueness is enforced **both** in the facade (`DuplicateUserName`/`DuplicateEmail`) and by the DB unique index.
- A capability that isn't registered is **absent** — resolving its service fails at resolution, rather than
  silently no-op'ing. Optional collaborators (lockout inside sign-in, tokens inside passwords) switch steps on or off.
- Credential changes (password, email, role or claim removal, 2FA changes, unlinking) rotate the security stamp, which
  ends every session of the person.
- Deleting a user removes its role, claim, login and token rows in the same save; the schema has no foreign keys.
- `IdentityDbContext` registers through the SDK context path, so the host's EF interceptors (audit stamps for
  `CreatedAt` / `UpdatedAt`) attach to it as to the host's own context.
- `IdentityResult.ToValidationError()` turns failures into field errors (code + values) that error translation matches.
- Entry point is **`AddUserAccounts`** (not `AddIdentityCore` as the deep-dive drafted) — ASP.NET's own
  `AddIdentityCore<TUser>` extension is always in scope via the shared framework and would collide. `IdentityUser`,
  `IdentityRole`, `IdentityBuilder`, `IdentityResult` and `IdentityError` share names with `Microsoft.AspNetCore.Identity`
  types; a file importing both namespaces qualifies them.
- User updates preserve the loaded tracked instance. A detached replacement with the same key is rejected while
  the original is tracked; a detached full-state update remains supported in a fresh context.

## See also

- Architecture + build order: [`engineering/research/identity/identity-architecture.md`](../../../../../research/identity/identity-architecture.md)
- Data layer this persists through: the be sdk `Data/` (`AppDbContextBase`, `AddEntityFrameworkCore`, audit interceptor) and its migrator (`Data/Migrations/Bespoke/bespoke.md` § Migration sets).

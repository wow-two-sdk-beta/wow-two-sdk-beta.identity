# T3 — Identity code and consumers

*Last updated: 2026-10-26*

> What the be sdk, the fe sdk and every product ship for auth today, set against the spec (now `product/product.md`): P1 protocol slices I17–I19 do not exist; Ocharo is the largest migration.

Path aliases (public repos):

- `be` = `wow-two-sdk-beta/wow-two-sdk-beta.be` → `engineering/codebase/wow-two-sdk-beta-be/src`
- `fe` = `wow-two-sdk-beta/wow-two-sdk-beta.fe` → `engineering/codebase/wow-two-sdk-beta-fe/src`
- Products are private repos: this file names each product and what it needs, never its paths or code.
- Latest published be sdk: `10.0.76-beta` (carries I13); fe sdk `0.0.32`.

---

## A · be sdk Identity

### Test run

- `dotnet test Identity.Tests/WoW.Two.Sdk.Beta.Be.Identity.Tests.csproj`, run 2026-10-25 from `be`.
- Result: 113 passed, 0 failed, 0 skipped, 3 s.
- 95 `[Fact]`/`[Theory]` methods; no Docker; in-memory SQLite plus `WebApplicationFactory`.
- Spec says "about 92 tests"; real count is 113.

### Folders

| Folder (files) | Does | Maturity | Known issues |
|---|---|---|---|
| `ApiKeys/` (15) | marker-prefixed keys stored as hashes, `ApiKey` scheme (Bearer or `X-Api-Key`), scope policies, access-gate middleware | 17 tests; `api-keys.md` + `ApiKeys.spec.md`; used by Wheelhouse, TranscriptForge | `IApiKeyRepository` is product-owned, so each consumer writes its own; outside the Identity service scope |
| `Authorization/` (5) | principal allowlist (open when empty) + default-deny fallback on one scheme | no direct tests (6 via `AllowlistedSessionOrApiKeyAuthorizationTests`); `authorization.md` + spec + standard; used by Wheelhouse, Desk, 10 pilots | empty allowlist admits any authenticated user, so each host must guard it |
| `Claims/` (8) | `wt:*` claim normalizer across 17 OAuth providers (`IClaimsTransformation`) | 0 tests; `claims.md` + spec + standard; Wheelhouse only | 6 provider profiles best-effort and unverified (X, Yandex, VK, Amazon, Slack, Notion); moot once Identity is the only issuer |
| `Cookies/` (3) | `AddCookieAuthentication`, secure defaults, `Mvc`/`Api` challenge mode | indirect tests (stamp sign-out); `cookies.md`; 13+ consumers | options built eagerly, no `Identity:*` host-section binding unlike other modules |
| `CurrentUser/` (5) | tri-state view: user / guest / anonymous | 2 tests in `ConfiguredRegistrationTests`; ForeverPin | none found |
| `Guest/` (6) | anonymous capability cookie sealed by Data Protection | 4 tests; `guest.md` + spec; ForeverPin | key-ring loss loses every guest; guest-to-user merge lives in product code |
| `Core/` (140) | own user store, 11-table EF schema, slices below | see sub-rows; `core.md` + 15 slice docs | consumers must add an EF migration for each schema addition (I10, I13 already did); no foreign keys |
| `IdentityApi/` (1) | `AddIdentityApiEndpoints` preset over Microsoft `AddIdentityCore` + `MapIdentityApi` | 0 tests, 0 consumers | contradicts "own, not a wrapper"; superseded by `Core/Endpoints`; keeps `Microsoft.AspNetCore.Identity.EntityFrameworkCore` referenced |
| `Jwt/` (7) | bearer validation + `ITokenIssuer` HMAC issuance | 8 tests (registration only); `jwt.md`, `issuance.md` | issuance is HS256/384/512 only (`Jwt/Issuance/JwtTokenIssuer.cs:34-38`); `ValidAlgorithms` takes one value (`Jwt/JwtServiceCollectionExtensions.cs:40`); no JWKS; `IKeyProvider` named in `issuance.md` does not exist |
| `Mfa/` (5) | `ITotpService` (Otp.NET), `AddFido2WebAuthn` | indirect via TwoFactor and passkey tests | TOTP secret stored in `identity_user_tokens`; no cipher or Data Protection call found |
| `OAuth/` (23) | 17 provider wrappers with one shape + `wt:provider` stamp; Google ID-token authenticator | Google authenticator 1 test; wrappers 0 tests | only GitHub (Wheelhouse) and Google ID token (ForeverPin) have consumers; no callback-to-link route |
| `Oidc/` (2) | `AddOpenIdConnectAuthentication`: generic OIDC client, code + PKCE, userinfo claims | 0 tests, 0 consumers; `oidc.md` | client only; no back-channel logout, no org claims; needs `AddCookieAuthentication` first |
| `Otp/` (40) | `IOtpService`, generator, per-culture formatter, channels `sms` · `whatsapp` · `telegram` · `telegram-gateway` · `email` | 7 tests; `otp.md` + 5 channel docs | memory store by default (`OtpServiceCollectionExtensions.cs:34`); `OtpRecord.Code` plaintext (`OtpRecord.cs:13`); resend gap default 5 s (`OtpOptions.cs:20`) |
| `PasswordHashing/` (2) | Argon2id `IPasswordHasher<TUser>` (m=19 MiB, t=4, p=1) | rehash covered in `PasswordSliceTests`; `argon2.md` | doc still shows ASP.NET `AddIdentityCore` wiring |
| `Policies/` (4) | `IRolePolicy` role-in-scope dictionary | 0 tests; 0 consumers found | superseded by org roles and permissions (I13) |

### `Core/` sub-slices

| Slice | Tests | Notes |
|---|---|---|
| users, EF stores, principal factory | 3 + 7 | tables `identity_users` · `roles` · `user_roles` · `user_claims` · `role_claims` · `user_logins` · `user_tokens` · `refresh_tokens` · `passkeys` · `organizations` · `memberships` |
| passwords, tokens, email, lockout, stamps | 7 + 5 + 5 | rules 8–128, HIBP fails open, lockout 5 / 5 min (`LockoutOptions.cs:7,10`), tokens bound to stamp |
| roles, claims, sign-in, external logins | 7 | `UserLoginService` persists a link only; no OAuth callback route |
| `TwoFactor/` | 3 | TOTP, recovery codes (SHA-256), delivered methods over any OTP channel |
| `Phones/` | 1 | service only; no HTTP route, no fe screen |
| `EmailSignIn/` | in endpoint tests | link or code over OTP |
| `Passkeys/` | 6 | usernameless; needs shared Data Protection key ring |
| `RefreshTokens/` | 3 | rotation + family revoke; no device, IP or label columns; no list-by-user (`IRefreshTokenRepository.cs`) |
| `Organizations/` | 7 | I13; `org_role` / `org_permission` claims; last-owner guard |
| `Endpoints/` | 7 | 28 routes (`UserAccountEndpointRouteBuilderExtensions.cs`); none for phone, organizations, sessions, Telegram |
| `PersonalData/` | in endpoint tests | export + erasure hooks |

### Suspects checked

| Suspect | Result |
|---|---|
| `init`-only options (CS8852 class of bug) | None. Every `*Options` property is `set` (110 setters, 0 `init`). `AllowlistOptions`, `ClaimNormalizationOptions`, `OtpOptions` are `set`; their dictionary and `Messages` members are get-only collections, which is fine. 145 `init` remain on request/response models and records; none on a configured options type. |
| Configured-path test coverage | Only `CurrentUser`, `Guest`, `Otp` have a configure-path test. `AddClaimNormalization(configure)` has none. `AddPrincipalAllowlist(configure)` is exercised only by Wheelhouse. |
| JWT HS256-only | Issuance: yes, symmetric only. Validation: asymmetric works via `MetadataAddress` (RS/PS/ES, HTTPS enforced). |
| OTP store memory-only | Default yes. Seam `IOtpRepository` exists. Haven and Desk each wrote a PostgreSQL store. |
| `<Compile Remove>` on OAuth leaves | None left. `WoW.Two.Sdk.Beta.Be.csproj` has 0 `Compile Remove`; its comment says the block is gone. Apple, GitHub, Microsoft compile. |
| OpenIddict | Pinned 6.0.0 at `Directory.Packages.props:162-163`; 0 references in csproj or code. |

### Code against I14–I22 and spec § Gaps

| Item | State | Already there | Missing |
|---|---|---|---|
| I14 invitations, admin-created members | not started | password-less user rows (`UserAccountService.CreateAsync`); `OrganizationService.AddMemberAsync` for existing users | invitation entity, single-use link, accept-on-sign-in, phone/email pre-registration; Desk's link-request table is the prototype |
| I15 durable OTP | not started | seam `IOtpRepository`; Haven's and Desk's own stores | one SDK store; hashed at rest; per-phone / IP / client limits |
| I16 sessions, sign-in log | not started | `RevokeFamilyAsync`, `RevokeUserAsync`; 6 log messages in `IdentityLogExtensions.cs`; `Foundation/Audit` hash chain; Desk's session table | session list, device columns, sign-in log entity, security-event store |
| I17 signing keys, JWKS | not started | validation side reads JWKS through `MetadataAddress` | key store, rotation with overlap, JWKS endpoint, asymmetric issuer, `Foundation/Security` envelope cipher for key storage |
| I18 OIDC server | not started | OpenIddict pin only | authorize, token, end-session, discovery, back-channel logout, client registry |
| I19 OIDC client module | partial | `Oidc/` (code + PKCE + userinfo), `Cookies/`, `Claims/`; Ocharo's SSO client (407 lines) as prototype | org claims, logout receiver, CSRF, per-app audience, refresh |
| I20 Telegram | not started | Telegram bot OTP channel, Gateway channel, `Telegram` login-provider convention in `TwoFactorMethodOptions.cs:19` | link-on-first-sign-in, `t.me/{bot}?start=` token, contact check, Log in with Telegram; Desk's Telegram endpoints (196 lines) are the prototype |
| I21 admin API | not started | `LockUntilAsync`, `RotateSecurityStampAsync`, `OrganizationService`, `UserRoleService` | HTTP surface; user list and search; registered-app admin |
| I22 password lifecycle | not started | rules, Argon2 rehash, HIBP | expiry, history, forced reset, temporary passwords, generator, strength score |
| Gaps · accounts | partial | organizations, memberships, roles (I13) | invitations, admin-managed members, callback-to-link route |
| Gaps · audit | not started | log lines only | sign-in log, admin trail |

---

## B · fe sdk auth

- Session core `fe/auth/`: `AuthProvider.vue`, `AuthContext.ts` (`useAuth`), `AuthSession.ts` (`unknown → resolving → authenticated | anonymous`).
- Bridge `fe/auth/AuthBridge.ts`: one expiry per session on first 401 or `bridge.expire()`; `subscribeExpired`; spec `AuthBridge.spec.md`.
- Strategies: `CookieStrategy.ts` (me-resolve, default `/api/identity/me`, sign-out `/api/identity/sign-out`), `BearerStrategy.ts`, `RedirectStrategy.ts`, `TokenStorage.ts`.
- Router glue `fe/router/adapters/vueRouter/Guards.ts`: `requireAuth`, `signInRedirect`, `buildReturnTo`/`useReturnTo`.
- Expiry notice `fe/feedback/FeedbackSessionExpiry.ts`: `feedbackSessionExpiry`.
- Google: `fe/foundation/oauth/GoogleIdentity.ts` (`provideGoogleIdentity`, GIS script loader) + `fe/presentation/actions/googleSignInButton/GoogleSignInButton.vue`.
- Account menu `fe/presentation/nav/accountMenu/AccountMenu.vue` (503 lines): identity header, organization switcher (2+ orgs), locale, colour mode, versions, sign out.
- Inputs: `passwordInput/PasswordInput.vue` (reveal toggle), `pinInput/PinInput.vue` (6 cells, `complete` event), `phoneInput/PhoneInput.vue`, `telInput/TelInput.vue`, `emailInput/`.
- Strength: `feedback/passwordStrengthCallout/PasswordStrengthCallout.vue`, naive 0–4 score from length and character classes (`:16-25`); `forms/passwordStrength/` is an empty directory.
- Absent: sign-in, phone + code, Telegram link, passkey/WebAuthn helper, MFA setup, account page, members/sessions/sign-in-log tables, hosted-page theming loader (fe backlog line 174).
- `createRedirectStrategy` and `createBearerStrategy` have no importer in any product (grep over `*.ts`, `*.vue`).
- `PasswordStrengthCallout` has no importer in any product.
- `PinInput` is used only by Haven.
- Product usage: 8 pilots + ForeverPin use `createCookieStrategy` + `AuthProvider` + `createAuthBridge`.
- documentation-checker and retainer-balance use only `createAuthBridge`; Wheelhouse uses it with its own client; Ocharo Studio uses it too.

---

## C · Consumers

Effort: S = config and frontend swap; M = data move plus code change; L = several services, data and retired paths.

### Table

| Product | Auth methods | Session shape | be sdk · identity APIs used | Target | Effort | Notes |
|---|---|---|---|---|---|---|
| Wheelhouse (`wow-two-platform.wheelhouse`) | GitHub OAuth with a login allowlist; API keys for integrations | cookie, 8 h, API challenge mode | `10.0.71-beta` · `AddCookieAuthentication`, `AddGitHubAuthentication`, `AddClaimNormalization`, `AddPrincipalAllowlist`, `AddDefaultDenyAuthorization`, `AddApiKeyAuthentication`, `RequireAllowlistedSessionOrApiKeyScope` | stay Form 1 | S if ever moved | deploys Identity, so signing in through it is circular; no provider-token use found |
| Secrets Vault | admin password, bearer tokens | bearer | `10.0.56-beta` under the old id `WoW2.Sdk.Backend.Beta` · `UseArgon2PasswordHasher`, `AddJwtTokenIssuance`, `AddJwtBearerAuthentication` | retire | none | inactive, merging into Wheelhouse |
| ForeverPin (was smart-qr) | Google ID token (GIS), guest-first | cookie, 30 d sliding | `10.0.72-beta` · `AddGoogleIdTokenAuthenticator`, `AddCookieAuthentication`, `AddGuestSession`, `AddCurrentUser`, `AddSpaAntiforgery`; fe `0.0.26` | OIDC client of Form 3 (first, per spec) | M | uuid user ids stay the identity id; the Google subject becomes a Google login row; guest→user merge and subscriptions key on that id |
| Haven | Telegram bot code | a signed session token in a cookie, read by several services | `10.0.74-beta` · `AddJwtTokenIssuance`, `AddJwtBearerAuthentication`, `AddOtpService`, `AddTelegramOtpDelivery`, own `IOtpRepository` | OIDC client of Form 3, after the relaunch decisions | M–L | own user and phone tables, several phones per person (the SDK user has one `PhoneNumber`); its own OTP store; relaunch still open |
| First-ten pilots ×10: retainer-balance, documentation-checker, config-checker, customer-promises, epub-review, file-watch, podcast-readiness, procedure-review, training-seats, vendor-renewals | email + password | cookie, 8 h | `10.0.74-beta` · `AddUserAccounts<IdentityUser>`, `AddEntityFrameworkStores`, `AddArgon2Passwords`, `AddLockout`, `AddSignIn`, `AddSecurityStampValidation`, `AddPersonalData` (5 of 10), `AddCookieAuthentication`, `AddDefaultDenyAuthorization`; fe `0.0.27` | stay Form 1 until launch, then OIDC clients of Form 3 | S each | same SDK schema and Argon2 hash format, so rows move unchanged; each carries its own identity context and EF migration; none maps `MapUserAccountEndpoints` |
| TranscriptForge | API keys only; no user sign-in | `ApiKey` scheme + loopback gate | `10.0.74-beta` · `AddApiKeyAuthentication`, `UseApiKeyAccessGate` | OIDC client of Form 3 at hosted launch; keys stay Form 1 | S | its backlog plans Google sign-in on the SDK identity vector |
| Sift | none | none | `10.0.74-beta` · none | undecided | S | fe `AccountMenu` only; single-user versus multi-user is open |
| Hijinx | none for users; an opaque room bearer | custom scheme | `10.0.74-beta` · none | stay Form 1 | none | room membership checked per operation |
| Arcade | Telegram Mini App `initData` HMAC | none, per request | `10.0.74-beta` · none | stay Form 1; Log in with Telegram later | none | validated in command handlers |
| Ocharo Desk | password, Telegram bot code, operator accounts from a CLI | cookie, 8 h, a server-side session row checked on every request | `10.0.71-beta` · ASP.NET Core Identity `AddIdentityCore` (not SDK Core), `AddCookieAuthentication`, `AddDefaultDenyAuthorization`, `AddOtpService`, `AddTelegramOtpDelivery`, own `IOtpRepository`, `JwtTokenIssuer` for integration tickets | home of Ocharo's Form 2 instance; Desk becomes a client | L | see the Ocharo summary below |
| Ocharo Brand, Catalogue | Desk SSO through Ocharo's SSO client library, referenced in place | host-only cookie + in-memory grant | `10.0.71-beta` · the SSO client | OIDC clients of Ocharo Identity | S–M | SSO off by default; public and sign-in-free until Identity lands |
| Ocharo Studio, Marketing, Blog (editorial) | Desk SSO through copies of the same library | same | `10.0.71-beta` · the SSO client; Studio also an integration-ticket handler | OIDC clients of Ocharo Identity | M each | Marketing and Blog keep older ticket-introspection handlers |
| No auth found | PoseCoach, Matinee, ListingShelf, PbnStudio, Museums Gallery, TNIS, PRISM, Whiteout, Surface Pattern, wheelhouse-canary | — | — | decide at first need | — | grep of `*.cs` and `*.swift` |
| Out of scope | non-.NET projects and POCs | — | — | — | — | not .NET be sdk consumers |

### Ocharo — what retires or migrates

Ocharo's repos are private; this summary names what exists, not where it lives.

- Desk tables (15, one schema): accounts in ASP.NET Core Identity tables (text ids, PascalCase columns),
  organizations, memberships, sessions; Telegram links, link requests and codes (codes protected with Data
  Protection); SSO codes and grants.
- Maps to the SDK: organizations and memberships match `identity_organizations` and `identity_memberships` column for
  column; accounts need a snake_case rename; a Telegram link becomes a `Telegram` login row plus a confirmed phone;
  sessions, Telegram codes and SSO rows retire into I16, I15 and I18.
- Desk code to replace: account and session endpoints, operator provisioning, the Telegram bot (webhook, updates,
  polling, the link flow), the SSO broker (authorize, exchange, refresh, revoke), integration tickets with
  introspection, the MCP authentication handler, and their E2E tests and settings.
- Desk frontend: the login page, session gate, account settings, the integrations and MCP access panels, and the
  Telegram access helpers.
- Client side: one SSO client library (5 files, 407 lines: challenge, callback, back-channel, an in-memory session
  store capped at 10 000 entries); Brand and Catalogue reference it in place, Studio, Marketing and Blog carry
  byte-identical copies kept in step by a sync script.
- Older ticket-introspection handlers: 3, in Studio, Marketing and Blog.
- Frontend session clients: 4 (Catalogue, Brand, Studio, and one shared by Marketing and Editorial), plus Desk's own.
- The SSO docs and the sync tooling retire with them.

---

## Spec corrections

- § What already exists: "about 92 tests" → 113 cases (95 methods), all green.
- § What already exists omits organizations and memberships; they shipped as I13 (`identity_organizations`, `identity_memberships`, `OrganizationService`, `org_role` / `org_permission` claims) and are in `10.0.76-beta`.
- § Gaps · accounts lists "organizations, memberships" as missing; only invitations, admin-managed members and the callback-to-link route are.
- § Problem: Desk's broker does not keep sessions in memory; each app's SSO client does. Desk stores codes and grants durably.
- § Problem: five apps (Brand, Catalogue, Studio, Marketing, Blog/Editorial) use the broker; Brand and Catalogue reference the canonical project rather than a copy.
- § Gaps · protocol: "JWT is HS256 only" is true for issuance only; validation already accepts RS/PS/ES through `MetadataAddress`.
- § Abuse protection P1 "hashed at rest" and "30-second resend": the SDK default stores `OtpRecord.Code` plaintext and resends after 5 s; product-owned stores differ.
- § Ocharo slice: bot flow "stays consumer-owned" per `Otp/Telegram/telegram.md`; the spec moves it into Identity, so that doc needs a rewrite with I20.
- § Build path: OpenIddict 6.0.0 is pinned; whether it supports .NET 10 / EF Core 10 is unverified.
- § What already exists "phone OTP sign-in": exists as `UserPhoneService` only; `MapUserAccountEndpoints` has no phone route.
- fe line "`passwordStrength` is empty": correct, the directory `fe/presentation/forms/passwordStrength/` is empty; the working meter is `PasswordStrengthCallout`.
- be sdk backlog line 137 says "Organizations first"; I13 is done (`wow-two-sdk-beta/wow-two-sdk-beta.be/engineering/planning/backlog.md`).
- Lead docs for `Jwt`, `Cookies`, `Oidc`, `Mfa/*`, `PasswordHashing`, `IdentityApi`, `OAuth/*` still show `dotnet add package WoW.Two.Sdk.Beta.Be.Identity.*` per-folder installs; everything ships in `WoW2.Sdk.Beta.Be`.

---

## Findings

- The OIDC server spine is absent: I17 (keys, JWKS), I18 (authorize, token, discovery, logout) and I19 (full client) have no code; OpenIddict is pinned and unreferenced. Every Ocharo migration step and all Form 3 clients depend on them.
- Ocharo is the largest migration (L): Desk plus 5 broker clients, 4 frontend clients, 3 introspection handlers, 3 copied SSO clients, 15 Desk tables, the Telegram bot and MCP auth. Desk runs ASP.NET Core Identity with PascalCase columns, not SDK Core, so users need a rename and key-type decision (text ids).
- Desk and Ocharo's SSO client are working prototypes for I14, I15, I19, I20 (link request token, Data-Protected code store, PKCE client with CSRF, contact-linked bot). Port them rather than redesign.
- Slices I8–I13 (refresh tokens, 2FA, passkeys, email sign-in, account endpoints, organizations) have zero product consumers; they are proven only by 113 in-memory SQLite tests. The only production use of Core is the pilots' password + lockout + stamp set.
- Each SDK schema addition forces one EF migration per consumer database (pilots keep one frozen identity migration); Desk, Haven and ForeverPin use hand-written SQL migrations instead. Form 2/3 needs one agreed migrator before I14–I16 add tables.
- OTP defaults are weak for a hosted service: memory store, plaintext code at rest, 5 s resend, no per-IP or per-client limits.
- Phone and Telegram sign-in have services but no HTTP routes or fe screens; the account API has no organization, session or admin routes. P1 needs new endpoints, not only new slices.
- Sessions cannot be listed: refresh tokens carry no device, IP or label, and the repository has no list-by-user call. I16 adds columns and a repository method.
- Product pins lag: Ocharo and Wheelhouse on `10.0.71`, ForeverPin `10.0.72`, Haven/pilots/TranscriptForge `10.0.74`; none has I13 (`10.0.76`). fe pins span `0.0.26`–`0.0.31` against `0.0.32`.
- Wheelhouse should stay Form 1: it deploys the Identity image and must sign in without it.
- Haven's several phones per user do not fit the single `PhoneNumber` on `IdentityUser`; settle the model before its migration.
- Test gaps that matter before P1: no tests for the claim normalizer, the 17 OAuth wrappers, `Oidc/`, `Cookies/` or default-deny directly.
- fe sign-in kit is almost empty: `createRedirectStrategy` fits the OIDC redirect but has no importer; no passkey helper, no sign-in or MFA screens.
- TOTP secrets show no encryption at rest in code searched; `Foundation/Security` envelope cipher is available to reuse.
- `IdentityApi/` (ASP.NET wrapper) is dead weight with 0 consumers and 0 tests; removing it drops a package reference.

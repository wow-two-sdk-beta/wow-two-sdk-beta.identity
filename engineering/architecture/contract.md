# wow-two-sdk-beta.identity — Contract

*Last updated: 2026-10-27*

> One broker per capability, the same in every form. Rules: wow-two-ws/conventions/development/capability-forms.md
> § Contract. Target shape: [integration modes](../research/identity-analysis-2026-10/t4-integration-modes.md) (T4)
> § Contract; package cut: [library architecture](../research/identity-analysis-2026-10/t7-library-architecture.md) (T7).

## Status

- ⬜ Brokers: the T4 contract (`IMemberBroker` and five more, `ActorModel`, 7 `IEvent` records) is not built; the code
  moved from the be sdk exposes services and store ports, not brokers.
- ✅ Contract package: `Abstractions` holds what the moved code already shared across forms — the error catalogue and
  the organization claims — referencing the kernel only.
- ⬜ Conformance: `Identity.Tests.Conformance` and `packages/Testing` are empty until the first broker exists.
- ✅ OIDC wire: the Server package makes the service an OpenID Connect provider (code + PKCE, rotating refresh,
  discovery, JWKS, userinfo, end-session) whose tokens carry the claims below; Forms 2/3 sign in against it.

## Abstractions — the contract as moved

### Error catalogue

| Type | Holds |
|---|---|
| `IdentityErrorCodeConstants` | 43 stable codes the slices return as `IdentityError.Code`: user name and email, password rules and breach check, tokens, lockout, roles, logins, two-factor and recovery codes, phone codes, sign-in, refresh tokens, passkeys, email sign-in, organizations |
| `IdentityErrorTextConstants.Messages` | culture (`ru`, `uz`) → code → template; `{Name}` placeholders fill from the error's `Params` |

- Failures travel as Module's `IdentityResult` of `IdentityError { Code, Description, Params }`;
  `IdentityResult.ToValidationError()` turns them into core `FieldError`s that error translation matches by code.
- `AddUserAccounts` contributes the texts through core's `AddErrorTexts(culture, messages)`; the host catalog
  (`Validation:Translation:Messages`) still wins over them.
- T4 moves failures to `Result` / `AppError` from an Identity catalogue; the codes stay, the carrier changes.

### Organization claims

| Member | Contract |
|---|---|
| `OrganizationClaimTypeConstants.Id` = `org_id` | the active organization's id; an OIDC token carries one active organization |
| `OrganizationClaimTypeConstants.Role` = `org_role` | one claim per membership, valued `{organizationId}:{role}` |
| `OrganizationClaimTypeConstants.Permission` = `org_permission` | one claim per granted permission, valued `{organizationId}:{permission}` |
| `ClaimsPrincipal.GetOrganizationRole(organizationId, claimType)` | the role in that organization, or `null` |
| `ClaimsPrincipal.HasOrganizationPermission(organizationId, permission, claimType)` | whether the permission is granted there |

- Module's `IdentityClaimOptions` may rename `org_role` and `org_permission`; the readers take the type as a parameter.
  OIDC tokens always use the names above.

### Authentication methods

| Member | Contract |
|---|---|
| `AuthenticationMethodConstants.ClaimType` = `amr` | how the session signed in (RFC 8176), one claim per method in a principal, a JSON array in a token |
| `Password` `pwd` · `HardwareKey` `hwk` · `OneTimePassword` `otp` · `MultipleFactors` `mfa` | the values the Module's `SignInService` records: a password, a passkey, a one-time code (authenticator, delivered code, recovery code, email link) and a second factor |

- An app asks for step-up (such as `max_age=0` and a fresh `hwk`) by reading `amr` and `auth_time` from the ID token.

### Token claims (Server)

| Claim | ID token | Access token |
|---|---|---|
| `sub` (user id) · `sid` (sign-in session) · `amr` · `org_id` · `org_role` · `org_permission` | ✓ | ✓ |
| `name` (with `profile`) · `email`, `email_verified` (with `email`) | ✓ | ✓ |
| `auth_time` | ✓ | — |

- One active organization per token: the one the grant carries while the person still belongs to it, else the
  earliest membership. `org_role` is `{org}:{role}`; `org_permission` holds `{org}:{app}:{permission}` values of that
  role, kept to the client's permission namespace.
- Each client's access tokens carry its own `aud`; people are identified by `iss` + `sub`. Userinfo answers the same
  profile and organization claims, without `amr`, `auth_time` and `sid`.
- `sid` (`SessionClaimTypeConstants.SessionId`, Abstractions) is the id of the Identity sign-in session the grant came
  from, a uuid in `D` form, the same in the ID and access token and kept by refreshes. Revoking that session (the
  person's session list, sign-out, end-session, a stamp rotation) ends the grant: refresh answers `invalid_grant` and
  userinfo `invalid_token`; access tokens already issued live out their 10 minutes. A relying party keys its own
  session on `sid` to match a later back-channel logout (`flows/flows.md`).

## Seams by form, as moved

Namespaces are unchanged from the be sdk. Entities are not generic: one `IdentityUser`, one `IdentityRole`, uuid v7 keys.

| Form | Package | Entry | Public seams a host binds or calls |
|---|---|---|---|
| kernel | core | — | `ICurrentUserService` (`Id`, `Kind`), `UserKind` — read by core's Audit, SoftDelete and Idempotency |
| edge | Auth | `AddCookieAuthentication`, `AddApiKeyAuthentication`, `AddPrincipalAllowlist`, `AddDefaultDenyAuthorization`, `AddGuestSession`, `AddCurrentUser`, `AddJwtBearerAuthentication`, `AddJwtTokenIssuance` | `IApiKeyRepository`, `IGuestSessionService`, `ITokenIssuer`; `CookieCurrentUserService` implements the kernel's `ICurrentUserService` |
| 1 | Module | `AddUserAccounts(configure, configureDatabase)` → `IdentityBuilder`; `IdentityDbContext`; the `identity` migration set | store ports `IUserRepository`, `IRoleRepository`, `IUserRoleRepository`, `IUserClaimRepository`, `IUserLoginRepository`, `IUserTokenRepository`, `IRefreshTokenRepository`, `IUserPasskeyRepository`, `IOrganizationRepository`, `IUserSessionRepository`, `ISignInLogRepository` (EF defaults ship); hooks `IUserPasswordValidator`, `IAccountDeletionHandler`, `IPersonalDataExporter`, `IPwnedPasswordsClient`, `ISessionRevocationHandler` |
| 1 | Module | OTP: `AddOtpService`, `Add{Email,Sms,Telegram,TelegramGateway,WhatsApp}OtpDelivery`; `AddTotp`; `AddFido2WebAuthn` | `IOtpService`, `IOtpRepository`, `IOtpCodeGenerator`, `IOtpMessageFormatter`, `IOtpDeliveryHandler` (keyed by `OtpChannelNameConstants`), `IOtpDeliveryHandlerFactory`, `ITotpService` |
| 2/3 | Client | `AddOpenIdConnectAuthentication(OidcOptions)` | none: an OIDC client over cookies; brokers over the management API land here |
| service | Server | `AddOidcServer(configure)` and `AddDataProtectionRing(configure)` on the Module's builder; `MapOidcServerEndpoints()`; `Identity:Server` (issuer, sign-in path, lifetimes, keys, clients); `Identity:DataProtection` (ring certificates) | `ISigningKeySource` (the settings by default: inline PEM or file paths); `SigningCertificateGenerator` and the service's `keys generate` |
| providers | Providers | `Add{Provider}Authentication` × 17, `AddGoogleIdTokenAuthenticator`, `AddClaimNormalization` | `IGoogleIdTokenAuthenticator`; the normalized `wt:*` claim set (`NormalizedClaimTypeConstants`) |

### Form 1 services

| Slice (builder call) | Service |
|---|---|
| core | `UserAccountService`, `UserClaimsPrincipalFactory` |
| `AddArgon2Passwords` · `AddBreachedPasswordCheck` | `UserPasswordService`, `PasswordRulesValidator`, `BreachedPasswordValidator` |
| `AddUserTokens` | `UserTokenIssuer` |
| `AddEmailConfirmation` | `UserEmailService` |
| `AddLockout` | `UserLockoutService` |
| `AddSecurityStampValidation` | `SecurityStampValidator` |
| `AddRoles` | `RoleService`, `UserRoleService` |
| `AddUserClaims` | `UserClaimService` |
| `AddExternalLogins` | `UserLoginService` |
| `AddTwoFactor` | `UserTwoFactorService`, `UserTwoFactorMethodService` |
| `AddPhoneNumbers` | `UserPhoneService` |
| `AddRefreshTokens` | `RefreshTokenService` |
| `AddSessions` | `UserSessionService` |
| `AddSignInLog` | `SignInLogService` |
| `AddPasskeys` | `UserPasskeyService` |
| `AddOrganizations` | `OrganizationService` |
| `AddEmailSignIn` | `UserEmailSignInService` |
| `AddPersonalData` | `UserPersonalDataService` |
| `AddSignIn` | `SignInService` |
| `AddAccountEndpoints` + `MapUserAccountEndpoints()` | the account HTTP API below |

### Account HTTP API (Module)

Mounted by the host on a group such as `/account`; routes beyond the first nine appear only when their slice is
registered.

| Slice | Routes |
|---|---|
| always | `POST register` · `POST login` · `POST refresh` · `POST logout` · `GET confirm-email` · `POST resend-confirmation-email` · `POST forgot-password` · `POST reset-password` · `GET manage/info` |
| two-factor | `GET manage/2fa` · `POST manage/2fa/{authenticator,enable,disable,recovery-codes,preferred}` · `POST manage/2fa/methods/{method}/{send,enable}` · `POST login/two-factor` |
| personal data | `GET manage/personal-data` · `POST manage/delete-account` |
| email sign-in | `POST email-sign-in/send` · `POST email-sign-in` |
| passkeys | `POST passkeys/login/options` · `POST passkeys/login` · `GET manage/passkeys` · `POST manage/passkeys/options` · `POST manage/passkeys` · `DELETE manage/passkeys/{id}` |
| sessions | `GET manage/sessions` · `DELETE manage/sessions/{id}` · `DELETE manage/sessions[?keepCurrent=true]` |

- Sign-in answers a bearer session (JWT through `ITokenIssuer`, plus a refresh token when registered) or, with
  `?useCookies=true`, a cookie carrying a new session's `sid` when sessions are registered; every sign-in route takes an
  optional `?clientId=` for the sign-in log; failures are problem details, translated when error translation is on.
- `service/Identity.Api` hosts `AddUserAccounts` over its own PostgreSQL context with Argon2 passwords, sign-in,
  lockout, stamps, sessions, the sign-in log and organizations, and maps the account API at `/api/account` for cookie
  sessions (the sign-in the OIDC authorize endpoint reads). It registers no `ITokenIssuer`: its tokens come from the OIDC server, so bearer sign-in
  and the routes of unregistered slices are not served yet.

## Forms map

| Form | Implementation today | Wire |
|---|---|---|
| 1 | Module services over the consumer's `DbContext` | in process |
| 2/3 | Client: OIDC sign-in against an instance | OIDC (`/connect/authorize`, `/connect/token`, `/connect/userinfo`, `/connect/endsession`, discovery, JWKS) served by Server; no broker wire yet |
| fe | `@wow-two-beta/fe/auth` strategies in the fe sdk (default paths `/api/identity/*`); `fe-identity` still holds the template's greetings kit | the host's session routes |

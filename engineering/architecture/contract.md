# wow-two-sdk-beta.identity — Contract

*Last updated: 2026-10-26*

> One broker per capability, the same in every form. Rules: wow-two-ws/conventions/development/capability-forms.md
> § Contract. Target shape: [integration modes](../research/identity-analysis-2026-10/t4-integration-modes.md) (T4)
> § Contract; package cut: [library architecture](../research/identity-analysis-2026-10/t7-library-architecture.md) (T7).

## Status

- ⬜ Brokers: the T4 contract (`IMemberBroker` and five more, `ActorModel`, 7 `IEvent` records) is not built; the code
  moved from the be sdk exposes services and store ports, not brokers.
- ✅ Contract package: `Abstractions` holds what the moved code already shared across forms — the error catalogue and
  the organization claims — referencing the kernel only.
- ⬜ Conformance: `Identity.Tests.Conformance` and `packages/Testing` are empty until the first broker exists.

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
| `OrganizationClaimTypeConstants.Role` = `org_role` | one claim per membership, valued `{organizationId}:{role}` |
| `OrganizationClaimTypeConstants.Permission` = `org_permission` | one claim per granted permission, valued `{organizationId}:{permission}` |
| `ClaimsPrincipal.GetOrganizationRole(organizationId, claimType)` | the role in that organization, or `null` |
| `ClaimsPrincipal.HasOrganizationPermission(organizationId, permission, claimType)` | whether the permission is granted there |

- Module's `IdentityClaimOptions` may rename both claim types; the readers take the type as a parameter.

## Seams by form, as moved

Namespaces are unchanged from the be sdk. Generic parameters: `TUser : IdentityUser<TKey>`, `TKey` is `Guid` through
`AddUserAccounts<TUser>`.

| Form | Package | Entry | Public seams a host binds or calls |
|---|---|---|---|
| kernel | core | — | `ICurrentUserService` (`Id`, `Kind`), `UserKind` — read by core's Audit, SoftDelete and Idempotency |
| edge | Auth | `AddCookieAuthentication`, `AddApiKeyAuthentication`, `AddPrincipalAllowlist`, `AddDefaultDenyAuthorization`, `AddGuestSession`, `AddCurrentUser`, `AddJwtBearerAuthentication`, `AddJwtTokenIssuance` | `IApiKeyRepository`, `IGuestSessionService`, `ITokenIssuer`; `CookieCurrentUserService` implements the kernel's `ICurrentUserService` |
| 1 | Module | `AddUserAccounts<TUser>()` → `IdentityBuilder<TUser, TKey>`; `AddEntityFrameworkStores<TContext>()`; `ApplyIdentitySchema<TUser, TRole, TKey>()` | store ports `IUserRepository`, `IRoleRepository`, `IUserRoleRepository`, `IUserClaimRepository`, `IUserLoginRepository`, `IUserTokenRepository`, `IRefreshTokenRepository`, `IUserPasskeyRepository`, `IOrganizationRepository` (EF defaults ship); hooks `IUserPasswordValidator`, `IAccountDeletionHandler`, `IPersonalDataExporter`, `IPwnedPasswordsClient` |
| 1 | Module | OTP: `AddOtpService`, `Add{Email,Sms,Telegram,TelegramGateway,WhatsApp}OtpDelivery`; `AddTotp`; `AddFido2WebAuthn` | `IOtpService`, `IOtpRepository`, `IOtpCodeGenerator`, `IOtpMessageFormatter`, `IOtpDeliveryHandler` (keyed by `OtpChannelNameConstants`), `IOtpDeliveryHandlerFactory`, `ITotpService` |
| 2/3 | Client | `AddOpenIdConnectAuthentication(OidcOptions)` | none: an OIDC client over cookies; brokers over the management API land here |
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
| `AddRoles<TRole>` | `RoleService`, `UserRoleService` |
| `AddUserClaims` | `UserClaimService` |
| `AddExternalLogins` | `UserLoginService` |
| `AddTwoFactor` | `UserTwoFactorService`, `UserTwoFactorMethodService` |
| `AddPhoneNumbers` | `UserPhoneService` |
| `AddRefreshTokens` | `RefreshTokenService` |
| `AddPasskeys` | `UserPasskeyService` |
| `AddOrganizations` | `OrganizationService` |
| `AddEmailSignIn` | `UserEmailSignInService` |
| `AddPersonalData` | `UserPersonalDataService` |
| `AddSignIn` | `SignInService` |
| `AddAccountEndpoints` + `MapUserAccountEndpoints<TUser>()` | the account HTTP API below |

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

- Sign-in answers a bearer session (JWT through `ITokenIssuer`, plus a refresh token when registered) or, with
  `?useCookies=true`, a cookie; failures are problem details, translated when error translation is on.
- `service/Identity.Api` hosts `AddUserAccounts` over its own SQLite context but maps no account routes yet: mapping
  them needs a signing key and the sign-in slices configured per target.

## Forms map

| Form | Implementation today | Wire |
|---|---|---|
| 1 | Module services over the consumer's `DbContext` | in process |
| 2/3 | Client: OIDC sign-in against an instance | OIDC (`/authorize`, `/token`) once Server exists; no broker wire yet |
| fe | `@wow-two-beta/fe/auth` strategies in the fe sdk (default paths `/api/identity/*`); `fe-identity` still holds the template's greetings kit | the host's session routes |

# wow-two-sdk-beta.identity — Package registry

*Last updated: 2026-10-26*

> Every package this repo publishes, one version line per registry. Layer rules:
> wow-two-ws/conventions/development/repo/structure/domain-structure.md § Layers; the cut and its reasons:
> [library architecture](../research/identity-analysis-2026-10/t7-library-architecture.md) (T7).

## Packages

All NuGet packages share the line `10.y.z-beta` (tag `be-v*`); their floor on core is the `BackendSdkVersion` pin. Each
folder under a package is a namespace below `WoW.Two.Sdk.Beta.Be.Identity`, kept from the be sdk.

| Package | Layer | Holds | References | Heavy deps | Forms |
|---|---|---|---|---|---|
| `WoW2.Sdk.Beta.Be.Identity.Abstractions` | contract | `Core/`: `IdentityErrorCodeConstants` (43 codes), `IdentityErrorTextConstants` (their ru/uz texts); `Core/Organizations/`: `OrganizationClaimTypeConstants`, `OrganizationClaimsPrincipalExtensions` | the kernel `WoW2.Sdk.Beta.Be.Abstractions` only; no framework reference | none | all |
| `WoW2.Sdk.Beta.Be.Identity.Auth` | edge | `Cookies/`, `ApiKeys/`, `Authorization/` (allowlist, default deny), `Guest/`, `CurrentUser/` (`CookieCurrentUserService`), `Jwt/` (bearer validation, HMAC `ITokenIssuer`) | Abstractions, core | `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.IdentityModel.JsonWebTokens`, `.Tokens` | all |
| `WoW2.Sdk.Beta.Be.Identity.Module` | forms | `Core/` (`AddUserAccounts`, entities, `ApplyIdentitySchema`, EF stores, 17 slices, account endpoints), `Otp/` (service + 5 channels), `Mfa/` (TOTP, WebAuthn), `PasswordHashing/` (Argon2) | Auth, core | `Fido2.AspNet`, `Otp.NET`, `Konscious.Security.Cryptography.Argon2`, `Telegram.Bot` | 1 |
| `WoW2.Sdk.Beta.Be.Identity.Client` | forms | `Oidc/` (`AddOpenIdConnectAuthentication`, Authorization Code + PKCE) | Auth, core | `Microsoft.AspNetCore.Authentication.OpenIdConnect` | 2, 3 |
| `WoW2.Sdk.Beta.Be.Identity.Providers` | extension | `OAuth/` (17 provider wrappers, Google ID-token authenticator), `Claims/` (claim normalizer, `NormalizedClaimTypeConstants`) | core | 14 `AspNet.Security.OAuth.*`, `Microsoft.AspNetCore.Authentication.{Facebook,Google,MicrosoftAccount}`, `Google.Apis.Auth` | 1, M0 |
| `WoW2.Sdk.Beta.Be.Identity.Server` | extension | nothing yet: the OIDC server slices (I17–I19) land here | Module, core | none yet; OpenIddict 7.7.2 with its first slice | service only |
| `WoW2.Sdk.Beta.Be.Identity.Testing` | test kit | nothing yet: fakes, personas and broker conformance bases land with the contract's brokers | Abstractions, `WoW2.Sdk.Beta.Be.Testing` | xunit (via be Testing) | all |
| `@wow-two-beta/fe-identity` | fe | the template's `GreetingWidget` kit until the sign-in kit replaces it | peers `@wow-two-beta/fe` `>=0.0.n <0.1.0`, `vue` | none | all |

| Package | Files | LOC |
|---|---|---|
| Abstractions | 4 `.cs` | ~290 |
| Auth | 39 `.cs` · 11 docs | ~1.4k |
| Module | 184 `.cs` · 27 docs | ~8.4k |
| Client | 2 `.cs` · 1 doc | ~70 |
| Providers | 31 `.cs` · 10 docs | ~1.2k |
| Server, Testing | 0 | 0 |

- Only Abstractions is new code beyond the move: `IdentityErrorTextConstants` carries the texts core held in
  `Web/ErrorTranslation/FieldCodeMessageConstants.cs`; `AddUserAccounts` contributes them through core's `AddErrorTexts`.
- Server and Testing ship as empty assemblies so the family's ids exist from the first release.

## Consumers

| Consumer | Takes | Arrives transitively | `using` edits |
|---|---|---|---|
| Form 1 product (pilots ×10, Haven, Ocharo Desk) | Module (+ Providers) | Auth, Abstractions | 0 |
| Form 3 product (ForeverPin at its move, Ocharo apps) | Client | Auth, Abstractions | 0 |
| M0 product (Wheelhouse, TranscriptForge), ForeverPin today | Auth (+ Providers) | Abstractions | 0 |
| API resource (File Storage, Events, Harvest services) | Auth | Abstractions | — |
| product `Domain` / `Application` projects, other domains' packages | Abstractions | the kernel | 0 |
| `service/Identity.Api` | Module, Server, Providers, Client by `ProjectReference` | — | — |
| test projects | + Testing | — | — |

- Products pin the family with one `IdentitySdkVersion` beside `BackendSdkVersion`; core `≤ 10.0.81-beta` plus any
  Identity package defines the same types twice (CS0433), so the first Identity release floors core at the breaking release.

## Provenance

Moved from the be sdk `wow-two-sdk-beta.be` at commit `6f55cfee507f8e20c1bf0aafaeccdf716e0889b7` (`10.0.81-beta`),
paths relative to its `engineering/codebase/wow-two-sdk-beta-be/src/`. Code and docs copied unchanged except the notes
below.

| From | To |
|---|---|
| `Identity/Core/IdentityErrorCodeConstants.cs`, `Identity/Core/Organizations/OrganizationClaim{TypeConstants,sPrincipalExtensions}.cs` | `packages/Abstractions/Core/…` |
| `Web/ErrorTranslation/FieldCodeMessageConstants.cs` (texts) | `packages/Abstractions/Core/IdentityErrorTextConstants.cs` |
| `Identity/{Cookies,ApiKeys,Authorization,Guest,Jwt,CurrentUser}/` | `packages/Auth/…` |
| `Identity/Core/` (the rest), `Identity/{Otp,Mfa,PasswordHashing}/` | `packages/Module/…` |
| `Identity/Oidc/` | `packages/Client/Oidc/` |
| `Identity/{OAuth,Claims}/` | `packages/Providers/…` |
| `Identity.Tests/{Accounts/,UserAccountServiceTests.cs}` | `tests/Identity.Tests.Integration/` |
| `Identity.Tests/{ApiKeys/,Otp/,ConfiguredRegistrationTests.cs,GoogleIdTokenAuthenticatorTests.cs,GuestCapabilityTests.cs,JwtRegistrationTests.cs}` | `tests/Identity.Tests.Unit/` |
| `Foundation.Tests/Comms/SmsBrokerTests.cs` (2 OTP tests) | `tests/Identity.Tests.Unit/Otp/SmsOtpDeliveryTests.cs` |
| `Web.Tests/ErrorTranslation/ErrorTranslationTests.cs` (1 identity test) | `tests/Identity.Tests.Unit/ErrorTranslation/IdentityErrorTextTests.cs` |
| `engineering/research/identity/` (3 files) | `engineering/research/identity/` |

- Edits beyond the copy: four doc `cref`s in the contract that pointed into Module became `<c>` text; `AddUserAccounts`
  registers the texts; the identity test registers them through `AddUserAccounts` and is renamed for that; two links
  and three be sdk paths in the moved docs re-pointed.
- Tests: 116 cases (113 from `Identity.Tests`, 2 from `Foundation.Tests`, 1 from `Web.Tests`), all passing against a
  local core built without Identity.

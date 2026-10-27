# Identity

**Our own identity for every product: a user store, an OIDC provider, hosted sign-in and an admin console — as a
.NET library, a self-hosted image or a provided service, behind one contract.**

> **Status — v0.1 in progress.** The code is moving here from the be sdk. Nothing is published yet: no NuGet package,
> no npm package, no image. The OIDC server is not built. The shapes below are the plan, not a release.

---

## What it is

- **One contract.** Products code against Identity's brokers, claims and events; the form behind them is configuration.
- **Own store.** People, organizations, memberships, sessions and the sign-in log live in one `identity` schema we own.
- **Standard protocol.** OAuth 2.0 / OpenID Connect through [OpenIddict](https://github.com/openiddict/openiddict-core),
  checked by the OpenID Foundation conformance suite.
- **Phone-first sign-in.** Codes through your own Telegram bot, Log in with Telegram, Telegram Gateway, SMS or email —
  each a setting, worded in `uz`, `ru` and `en`.
- **Hosted or embedded UI.** Hosted sign-in, account and console pages, or Vue components embedded in your app.

---

## Three forms

| Form | You install | It runs | You own |
|---|---|---|---|
| **1 · Library** | the `Module` package | inside your app's process | your database's `identity` schema |
| **2 · Self-hosted service** | the `Client` package + the `identity` image | a container you deploy | the instance and its database |
| **3 · Provided service** | the `Client` package | one central instance we operate | your app's client settings |

Moving from Form 1 to Form 3 swaps the `Module` package for the `Client` and changes configuration; product code stays
the same.

---

## Artifacts

All of them are **not published yet**.

| Artifact | What it holds | Install |
|---|---|---|
| `WoW2.Sdk.Beta.Be.Identity.Abstractions` | the contract: brokers, models, events, claim constants, errors | `dotnet add package WoW2.Sdk.Beta.Be.Identity.Abstractions --prerelease` |
| `WoW2.Sdk.Beta.Be.Identity.Auth` | store-less auth: cookies, API keys, allowlists, guest, JWT validation | `dotnet add package WoW2.Sdk.Beta.Be.Identity.Auth --prerelease` |
| `WoW2.Sdk.Beta.Be.Identity.Module` | Form 1: the store, SQL migrations, OTP channels, passkeys, TOTP | `dotnet add package WoW2.Sdk.Beta.Be.Identity.Module --prerelease` |
| `WoW2.Sdk.Beta.Be.Identity.Client` | Forms 2 and 3: the OIDC client and the HTTP brokers | `dotnet add package WoW2.Sdk.Beta.Be.Identity.Client --prerelease` |
| `WoW2.Sdk.Beta.Be.Identity.Providers` | social sign-in: 17 OAuth providers and a Google ID-token check | `dotnet add package WoW2.Sdk.Beta.Be.Identity.Providers --prerelease` |
| `WoW2.Sdk.Beta.Be.Identity.Server` | the OIDC server: OpenIddict, signing keys, JWKS | `dotnet add package WoW2.Sdk.Beta.Be.Identity.Server --prerelease` |
| `WoW2.Sdk.Beta.Be.Identity.Testing` | fakes, personas, a code sink and the conformance base | `dotnet add package WoW2.Sdk.Beta.Be.Identity.Testing --prerelease` |
| `@wow-two-beta/fe-identity` | contract mirror, embeddable components and widgets, admin screens | `pnpm add @wow-two-beta/fe-identity` |
| `identity` image | the service: hosted pages, console, management API | `docker pull ghcr.io/wow-two-sdk-beta/wow-two-sdk-beta.identity/identity` |

- NuGet line `10.y.z-beta` from `be-v*` tags · npm line `0.0.z` from `fe-v*` tags · image `X.Y.Z` from `v*` tags.
- A product pins the family with one `IdentitySdkVersion` property beside `BackendSdkVersion`.

---

## Quick start

The registration below is the **proposed** shape (working names); it lands with the first release.

### Form 1 — in your process

```bash
dotnet add package WoW2.Sdk.Beta.Be.Identity.Module --prerelease
```

```csharp
builder.Services.AddIdentityIntegration(builder.Configuration.GetSection("Identity"));
app.MapIdentitySession(); // /api/identity/me, sign-in, sign-out, …
```

```json
{ "Identity": { "Mode": "Module", "Phones": { "Enabled": true } } }
```

The Module ships its SQL migrations; your deploy runs them before the new version serves.

### Form 2 — your own instance

```bash
docker run ghcr.io/wow-two-sdk-beta/wow-two-sdk-beta.identity/identity:<version>   # not published yet
dotnet add package WoW2.Sdk.Beta.Be.Identity.Client --prerelease
```

```json
{
  "Identity": {
    "Mode": "Oidc",
    "Authority": "https://id.example.com/",
    "ClientId": "my-app",
    "Audience": "my-app-api"
  }
}
```

The client secret comes from your secret store, never from the settings file.

### Form 3 — the provided instance

The same as Form 2, pointed at the central instance's authority; you run no container.

### Frontend

```bash
pnpm add @wow-two-beta/fe-identity
```

Embed the sign-in components on your own pages, or forward the flow to the instance's hosted pages and come back.

---

## Features

| Area | P1 — first slice | Later |
|---|---|---|
| Sign-in | phone + code via your own Telegram bot; Log in with Telegram | Gateway, SMS, email, passwords, passkeys, social, enterprise SSO |
| Members | invite-only, organizations, roles, app access, disable, remove | self sign-up, account linking |
| Sessions | code + PKCE, rotating refresh, key rotation, back-channel logout | device flow, token exchange |
| Security | durable hashed codes, rate limits, operator passkey or TOTP | step-up, new-device alerts, risk scoring |
| Console | members, sessions, sign-in log, counts | policies, charts, impersonation |
| Developers | the Client, embeddable screens, test mode, a claims hook | management API, webhooks, SCIM |

---

## Docs

| Read | For |
|---|---|
| [product/product.md](product/product.md) | the capability: problem, forms, packages, model, phases |
| [product/context.md](product/context.md) | where it stands, open questions, the decision log |
| [product/features/](product/features/features.md) | one spec per feature group |
| [product/flows/flows.md](product/flows/flows.md) | sign-in, sign-out and member management, step by step |
| [engineering/architecture/architecture.md](engineering/architecture/architecture.md) | forms map, package graph, the service |
| [engineering/architecture/decisions/decisions.md](engineering/architecture/decisions/decisions.md) | confirmed decisions with dates |
| [engineering/research/research.md](engineering/research/research.md) | the analysis behind the design |
| [engineering/planning/backlog.md](engineering/planning/backlog.md) | what is planned, by phase |
| [engineering/scripts/scripts.md](engineering/scripts/scripts.md) | how versions and releases work |

---

## Develop

```bash
# Service, then the pages and console proxied to it
cd engineering/codebase/identity.backend-services && dotnet run --project service/Identity.Api --launch-profile https
cd engineering/codebase/identity.frontend-services && pnpm install --frozen-lockfile && pnpm dev

# Backend tests: unit, integration, E2E, conformance and migrations (only migrations needs Docker; the build needs Node and pnpm)
cd engineering/codebase/identity.backend-services && dotnet test Identity.BackendServices.slnx

# Frontend: lint, tests, build, packed-consumer check
cd engineering/codebase/identity.frontend-services && pnpm lint && pnpm test && pnpm build && pnpm check:package
```

---

## Status

- **v0.1 — Identity on its own:** the split from the be sdk. In progress.
- **Next — the Ocharo slice:** invitations, durable codes, sessions and the sign-in log, signing keys, the OIDC
  server and client, the Telegram link.
- Issues and discussions are off; this repo is developed in the open as part of the wow-two SDK.

## License

[MIT](LICENSE)

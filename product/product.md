# Identity — Capability spec

*Last updated: 2026-10-26*

> Our own user store, OIDC provider, hosted sign-in and admin console, shipped in three capability forms under one
> broker contract. Every product signs in through it instead of wiring Google, Telegram or passwords itself.
> State and decisions: [context.md](context.md); features: [features/](features/features.md); rules:
> `wow-two-ws/conventions/development/capability-forms.md`.

## Problem

- sign-in is built per product four ways: Wheelhouse GitHub, ForeverPin Google, Haven Telegram code, the first-ten
  pilots email and password; Ocharo Desk has a fifth (password + Telegram code + its own SSO broker).
- Ocharo duplicates the client side: one SSO client library referenced by two apps and copied into three more, plus
  four frontend session clients and three older ticket-introspection handlers.
- Desk's broker is not OIDC; each app's SSO client keeps its sessions in memory (a restart signs everyone out, one
  instance only); Desk has no member administration: operators come from a CLI command, memberships from fixtures.
- one Google client per product does not scale to the portfolio; one shared client leaks token audiences.

---

## Forms

| Form | Ships | Named consumer | State |
|---|---|---|---|
| 1 · library | `WoW2.Sdk.Beta.Be.Identity.Module` | the first-ten pilots (email + password, in-process) | code moving out of the be sdk |
| 2 · self-hosted service | `…Identity.Client` + the `identity` image | Ocharo: its own instance, bot, brand, members and database | planned, P1 |
| 3 · provided service | `…Identity.Client` | the portfolio: ForeverPin first, then the pilots at launch | planned, after P1 |

- one broker contract in `…Identity.Abstractions` serves every form; a product changes form by configuration.
- one registration reads `Identity:Mode = Module | Oidc` — proposed shape: a shared `IdentityMode` enum in
  Abstractions, options bound from configuration and overridable in code (Q2, confirmation pending).
- a Form 1 → 3 move swaps the `Module` package for the `Client`; the Client's brokers delegate to the running server.
- the service hosts the Form 1 module; no identity logic lives only in the service.
- Form 3 runs on a WoW2 products host, never the platform's control host; none exists yet (Q4).
- one instance per isolation boundary — owner, product or residency (D2); one person per instance (D3).
- the image follows the deployment descriptor (`wow-two-ws/conventions/deployment/descriptor/deploy-descriptor.md`).
- apps integrate through the one `Client` whatever the form; it replaces Ocharo's SSO client, Desk's integration
  tickets and every per-app session client.

---

## Packages

| Package | Holds | Used by |
|---|---|---|
| `WoW2.Sdk.Beta.Be.Identity.Abstractions` | the contract: brokers, models, events, claim constants, errors | every form; product Domain and Application layers |
| `…Identity.Auth` | store-less auth: cookies, API keys, allowlist, guest, JWT validation, the session facade | Module, Client; API resources |
| `…Identity.Module` | Form 1: the store, slices, SQL migrations, OTP channels, passkeys, TOTP | Form 1 products; the service |
| `…Identity.Client` | Forms 2/3: OIDC client, HTTP brokers, logout and webhook receivers | Form 2 and 3 products |
| `…Identity.Providers` | upstream social sign-in: 17 OAuth providers, Google ID token | products that need them; the service |
| `…Identity.Server` | the OIDC server: OpenIddict, keys and JWKS, client registry, back-channel sender | the service |
| `…Identity.Testing` | fakes, personas, code sink, the conformance base | test projects |
| `@wow-two-beta/fe-identity` | contract mirror, embeddable components and widgets, forward to the hosted pages | product frontends; `apps/web` |

- the contract references only the kernel `WoW2.Sdk.Beta.Be.Abstractions` (`Result`, `AppError`, `IEvent`,
  `ICurrentUserService`); the be sdk never references an Identity package.
- session and transport strategies (`auth`) stay in `@wow-two-beta/fe`.
- the package graph and the service's role: [architecture](../engineering/architecture/architecture.md).

---

## Build path

- **D1, decided 2026-10-21:** OpenIddict (Apache-2.0, .NET) for the protocol, on our own store; 7.7.2, which needs
  EF Core 10.0.11 or later.
- what it does: authorize, token, discovery and JWKS endpoints, PKCE, refresh, client credentials, device flow and
  token exchange; our store keeps the users.
- where: the `Server` package, loaded by the service (Forms 2 and 3); a Form 1 product never loads it, unless it opts
  into the in-process OIDC server.
- what it lacks: back-channel logout and signing-key rotation; slices I16–I18 build both.
- why not Keycloak, Zitadel, Logto or Authentik: each replaces our store, has no Form 1, and custom flows such as the
  Telegram link need Java SPIs or Go actions; Duende needs a commercial licence.
- the risk of owning auth is protocol correctness: OpenIddict carries the protocol, and the OpenID Foundation
  conformance suite runs in CI before Form 2 ships.

---

## Model

The contract's nouns and verbs; the broker as built: [contract](../engineering/architecture/contract.md).

| Noun | Is | Verbs |
|---|---|---|
| person | one account per instance; its id is `sub` (uuid v7) | get, find by phone, email or Telegram id |
| organization | a tenant | get, list for a person, create, rename, delete |
| membership | person ↔ organization: one role, per-app access | change role, set app access, suspend, reinstate, remove |
| invitation | an admin-added member by phone or email | invite, list, resend, revoke |
| session | one signed-in browser or device; its id is `sid` | list, revoke one, revoke all |
| sign-in attempt | one row of the sign-in log | list by organization, person, app, time and result |
| app | a registered client: redirect URIs, methods, theme | get, upsert from its deploy descriptor |

- proposed brokers (T4): `IMemberBroker`, `IOrganizationBroker`, `IInvitationBroker`, `ISessionBroker`,
  `ISignInLogBroker`, `IAppBroker`; every write takes an actor; operations return `Result` with an Identity `AppError`.
- events, the same in every form: `MemberAdded`, `MemberChanged`, `MemberRemoved`, `PersonChanged`,
  `PersonDeleted`, `SessionEnded`, `SignedIn`.
- claims: `sub`, `sid`, `name`, `email`, `phone_number`, `locale`, `org_id` (the active organization), `org_role`,
  `org_permission`, `amr`.
- out of the contract: interactive sign-in, invitation acceptance, the Telegram link, MFA, passkey ceremonies and
  password reset — hosted pages or embedded flows; no broker sets a person's credentials.

---

## Features

The whole vector, phased: P1 is the Ocharo slice, P2 completes the vector, P3 waits on a named consumer. One doc per
group in [features/](features/features.md); state per group in `engineering/planning/backlog.md` § Features.

---

## Ocharo slice (P1)

- **member added** — the admin enters name, phone and role in the console; no password exists.
- **first sign-in** — the member enters their phone; the page shows "Open Ocharo bot" with a
  `t.me/{bot}?start={one-time token}` link; the bot asks to share the contact, checks it is the sender's own and
  matches the invited phone, stores the link and sends the code.
- **every later sign-in** — phone, then the code from the bot; or one tap on Log in with Telegram.
- **member removed** — every session and refresh token is revoked at once; apps sign out by back-channel logout.
- after the first link, identity keys on the Telegram user id, never on the phone claim (Q14).
- the bot's webhook moves from Desk to Ocharo Identity; other services may still send through the Bot API (D5).
- migration: Desk's users, organizations, memberships and Telegram links move into Ocharo's own Identity database by a
  one-time conversion (Q55); Desk, Catalogue, Brand, Studio, Marketing and Editorial become OIDC clients;
  integration tickets, the SSO broker and the copied SSO clients retire. Catalogue's planned sign-in comes with it.
- step-by-step flows: [flows](flows/flows.md).

---

## Phases

0. Split: Identity leaves the be sdk for this repo — code, tests, docs and research (v0.1).
1. Packages: OIDC server, invitations, durable OTP and session stores, sign-in log, asymmetric keys with JWKS,
   Telegram link-on-first-sign-in, the OIDC client.
2. `fe-identity`: phone, code and Telegram link screens; members, sessions and sign-in log tables for the console.
3. Service: host, hosted pages, admin console, image, `deploy.yml`, the conformance suite.
4. Ocharo: deploy its instance, migrate Desk's accounts, move the apps one by one, retire the old paths.
5. P2 across the vector; then Form 3 for the portfolio, starting with ForeverPin and the pilots.

- each phase lands as version-track entries in `engineering/planning/`; the cross-capability order stays in
  `wow-two-ws/docs/platform-apps.md`.

---

## Sources

- [Identity analysis 2026-10](../engineering/research/identity-analysis-2026-10/identity-analysis-2026-10.md) — the
  evidence and the decision queue.
- `engineering/research/identity/` — the slice log I1–I22 and the architecture notes carried over from the be sdk.
- [Log In With Telegram](https://core.telegram.org/bots/telegram-login) — OIDC flow, `phone` scope, BotFather client.
- [Telegram Gateway API](https://core.telegram.org/gateway/api) — paid codes to any Telegram phone number.

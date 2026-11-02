# Identity — Product Context

*Last updated: 2026-11-02*

> Current state — changes often. Durable definition: [product.md](product.md).

## Current state

v0.5 (Ocharo sign-in) is in progress; v0.1–v0.4 are closed. `main` at `2376611` ships NuGet `10.0.17-beta`, npm
`@wow-two-beta/fe-identity` `0.0.7` and image `v0.5.7` (2026-11-02). The first consumer is Ocharo: Desk signs in
through Ocharo's own instance with the `Client` BFF. Track: [v0.5](../engineering/planning/version-track/v0.5/v0.5.md).

### What exists

- methods: password with email confirmation (SMTP, Mailpit locally), phone code by Ocharo's Telegram bot (webhook
  hosted, polling locally), passkeys, email link and code, TOTP and delivered second factors, recovery codes.
- settings: every method, verification and policy from `Identity:*`; `config check`; a public declaration
  `GET /api/account/sign-in-options`; password change confirmation `None` | `Code` | `Link`.
- sessions: `Identity:Sessions:Expiration` `Fixed` (12 h, or 30 days with "Keep me signed in") or `Sliding`; the
  cookie, every OIDC refresh chain, each BFF session and a bearer refresh family end with the session.
- sign-up: `Identity:SignUp:Mode` `Invite` (default) or `Open`; single-use invitation links with a fixed role and an
  optional bound phone or email; member routes at `/api/organizations/{id}`; the fixed role catalog `owner`, `admin`,
  `editor`, `viewer`, `billing`.
- protection: lockout for new attempts only, hashed codes with a per-number budget, `identity-credentials` rate limit,
  bounded Argon2, `prompt=login` / `max_age`, end-session safe from other sites.
- surfaces: the OIDC server (OpenIddict), the `Client` BFF, `fe-identity` `./kit` (password, phone, second factor,
  password forms), the hosted pages in one animated card, `admin create` for the first owner.

### Gaps

- the members panel (`./admin`) and the console; open sign-up's guest and email-first form (C13); the layout (C14).
- back-channel sign-out (I18), app access checks, client credentials and agent tokens, conformance in CI.
- a built-in BFF session store; until a product registers a shared cache and key ring, its deploys sign people out.
- local runtime: arm64 image, HTTPS from a mounted PEM, the compose build, phone sign-in tested with a real bot.

---

## Active tasks

| Task | Status | Notes |
|---|---|---|
| Ocharo's instance on v0.5.7 with the new settings | ⬜ | drop `Identity:SignUp:IsEnabled`; `SignUp:Mode=Invite`; `Sessions` sliding 5 days; roles from the catalog only; driven from the Ocharo chat |
| Desk's BFF on `10.0.17-beta` with a shared cache and key ring | ⬜ | Ocharo's side ([Client README](../engineering/codebase/identity.backend-services/packages/Client/README.md)) |
| Phone sign-in on a local run with Ocharo's local bot | ⬜ | the developer writes `engineering/deployment/identity.local.env`; then restart the stack |
| Sign in to Ocharo dev by both methods | ⬜ | v0.5 verification |
| Members panel, invite by phone | ⬜ | v0.5 Iteration 4; the routes exist |
| Open sign-up and the layout | ⬜ | after C13 and C14 |
| Local runtime items | ⬜ | v0.5 Iteration 5 |

---

## Open questions

- C4 — the console signs in by the instance's own cookie, opened from Wheelhouse through one-time links, keeping no
  password: yes?
- C13 — open sign-up's "guest" is an account with no organization role, through an email-first form: yes?
- C14 — the hosted pages' layout: what should change?
- P1, P3–P8 — the [Ocharo shortlist](../engineering/research/ocharo-shortlist-2026-10/ocharo-shortlist-2026-10.md)'s
  remaining points (permissions per app, start tokens, phones, operator factor, registration shape, G1 scope,
  offboarding).
- Q2, Q4, Q7–Q11, Q13, Q19, Q25–Q28, Q30 — the
  [identity analysis](../engineering/research/identity-analysis-2026-10/identity-analysis-2026-10.md) § Points.
- C-points with evidence: [sign-in configuration](../engineering/research/sign-in-config-2026-10/sign-in-config-2026-10.md) § Points.

---

## Decisions

- 2026-10-21 — build our own Identity on our own store; Ocharo first, with phone + Telegram bot code and
  admin-managed members.
- 2026-10-21 — D1: OpenIddict carries the protocol; D2: one instance per owner, Ocharo runs its own; D3: one person
  per instance, roles per organization, app permissions as claims.
- 2026-10-25 — D4: every code channel is configuration — own bot, Telegram Gateway, SMS, email — worded by
  `IOtpMessageFormatter` in `uz`, `ru` and `en`.
- 2026-10-25 — the SDK splits into one repo per domain, Identity first; shared seams live in the kernel
  `WoW2.Sdk.Beta.Be.Abstractions`; a domain's spec and analysis live in its repo, the hub keeps pointers.
- 2026-10-25 — P1 adds test mode with fake channels, an operator passkey or TOTP, a claims-contributor hook,
  OpenTelemetry and health.
- 2026-10-25 — every password policy is configurable, all off by default, minimum length 15.
- 2026-10-26 — three forms under one broker contract; seven packages: Abstractions, Auth, Module, Client,
  Providers, Server, Testing; a Form 1 → 3 move swaps the Module package for the Client.
- 2026-10-26 — package versions come from tags with no release commit; a nightly job tests against the newest core.
- 2026-10-26 — `@wow-two-beta/fe-identity` ships embeddable components and widgets and can forward a flow to the
  hosted pages; `auth` stays in `@wow-two-beta/fe`.
- 2026-10-26 — the whole identity schema is always created and configuration switches only reads and writes; one SQL
  migration set in the Module; schema `identity` plus the `identity_` prefix; never two instances on one schema.
- 2026-10-26 — D2 widens to one instance per isolation boundary (owner, product or residency); production migrations
  run as an explicit deploy step.
- 2026-10-28 — Ocharo's instance is a service of Ocharo's product, its schema in Ocharo's database; wow2's shared
  instance is a Wheelhouse product of its own. Products may key into Identity's published tables.
- 2026-10-26 — breaking changes are allowed while the products have no users.
- 2026-10-26 — the management screens ship in `@wow-two-beta/fe-identity`'s `./admin` export; `apps/web` composes
  them into the console, and a central services console or a product's admin page imports the same export.
- 2026-10-27 — after the spine, Ocharo's slice comes before wow2's hosted pages: shortlist, close the gaps, deploy
  with Ocharo and verify; the rest of P1 and the console layout review follow.
- 2026-11-01 — the flows other apps need ship and deploy first, Ocharo goes live on them, the console comes after; no
  test mode in the runtime; one look per instance; routes say sign in, sign up and sign out.
- 2026-11-02 — sessions end by `Fixed` (default and extended) or `Sliding` lifetimes, and everything issued under a
  session ends with it; sign-up is by invitation only unless `Open`; the role catalog is fixed in code.
- 2026-11-02 — a lockout refuses new attempts only and never signs anyone out; phone codes carry a per-number budget.
- every architecture decision with its source: [decisions](../engineering/architecture/decisions/decisions.md).

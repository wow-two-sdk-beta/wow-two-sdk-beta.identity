# Identity — Product Context

*Last updated: 2026-10-26*

> Current state — changes often. Durable definition: [product.md](product.md).

## Current state

v0.1 is in progress: the repo was created from the domain template on 2026-10-26, and Identity's code, tests and
research are moving here out of the be sdk; the docs moved in the same pass. Nothing is published yet — no NuGet
package, no npm package, no Identity image — and the OIDC server is not built. The first consumer is Ocharo (Form 2).

### What exists

From the be sdk's `Identity/` (113 tests, green 2026-10-25), now moving into the packages:

- email + password: Argon2id with rehash on upgrade, length 8–128, identifier check, breached-password check (fails
  open), lockout 5 attempts / 5 minutes, reset tokens bound to the security stamp.
- organizations and memberships with roles and permissions as claims (I13).
- email magic link and email code; phone OTP sign-in as a service (no HTTP route yet); OTP channels SMS (Twilio,
  Vonage, Eskiz), email, WhatsApp, Telegram bot message, Telegram Gateway.
- passkeys (Fido2, usernameless), TOTP, hashed recovery codes, delivered-code second factors, API keys.
- 17 OAuth registrations (Amazon, Apple, Discord, Facebook, GitHub, GitLab, Google, LinkedIn, Microsoft, Notion,
  Reddit, Slack, Spotify, Twitch, X, VK, Yandex) and a generic OIDC client.
- cookie or JWT sessions; refresh tokens rotate and a reuse revokes the family.
- fe sdk: auth bridge and strategies, Google button, `accountMenu`, `pinInput`, `passwordInput`, a naive strength
  callout.
- Ocharo Desk holds working prototypes: organizations and memberships (`owner`, `editor`, `viewer`), Telegram links,
  the bot's link-by-contact flow with 6-digit codes, an 8-hour session table, PKCE app codes.

### Gaps

- protocol — OIDC server endpoints, discovery, JWKS, asymmetric signing keys and rotation (issuance is HS256 only;
  validation already accepts RS, PS and ES keys).
- accounts — invitations, admin-managed members, an OAuth callback-to-link route.
- passwords — expiry, history, forced reset, admin-generated temporary passwords, generator, a real strength meter.
- sessions — a durable OTP store (memory only today, codes plaintext at rest, 5-second resend), a session list per
  user and device, revoke one or all.
- audit — sign-in log, security events (log lines only today), admin action trail.
- admin — user admin API, disable, impersonation, the console itself.
- Telegram — Log in with Telegram (OIDC), link-on-first-sign-in without a password.
- fe — sign-in, phone + code, Telegram link, passkey, MFA and account screens; `passwordStrength` is empty.

---

## Active tasks

| Task | Status | Notes |
|---|---|---|
| Move the code, tests and research out of the be sdk | 🔄 | another lane; `contract.md` and `package-registry.md` follow from the moved code |
| Move the spec and the analysis batch into the repo | 🔄 | this pass; uncommitted until the sanitization review |
| First npm publish of `@wow-two-beta/fe-identity` | ⬜ | by hand; then its trusted publisher |
| NuGet trusted-publishing consent for the seven ids | ⬜ | by hand |
| Wheelhouse registration, secrets and DNS for an instance | ⬜ | by hand; waits on Q4, Q5 and Q25 |
| Migrator follow-ups before the first deploy | ⬜ | owner marker naming the instance, baseline, multi-set harness |
| P1 slices I14–I20 | ⬜ | [backlog](../engineering/planning/backlog.md) |

---

## Open questions

- Q2 — confirm the registration shape: `Identity:Mode = Module | Oidc`, a shared `IdentityMode` enum, options from
  configuration overridable in code.
- Q4, Q5 — the Form 3 host, and how Wheelhouse models two owners' instances of one image.
- Q7–Q11 — browser mode, organization scope in tokens, revocation, front-channel logout, management authorization.
- Q13 (D5) — Identity owns the Ocharo bot's webhook.
- Q19, Q25–Q28, Q30 — hosted-page headers, signing keys, the Data Protection ring, counsel on the telecom clause,
  conformance in CI, domain binding and backups before Ocharo's cutover.
- the full queue with evidence: [identity analysis](../engineering/research/identity-analysis-2026-10/identity-analysis-2026-10.md) § Points.

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
- 2026-10-26 — D2 widens to one instance per isolation boundary (owner, product or residency); Ocharo gets its own
  database; production migrations run as an explicit deploy step; SQLite stays a supported Form 1 dialect.
- 2026-10-26 — breaking changes are allowed while the products have no users.
- 2026-10-26 — the management screens ship in `@wow-two-beta/fe-identity`'s `./admin` export; `apps/web` composes
  them into the console, and a central services console or a product's admin page imports the same export.
- every architecture decision with its source: [decisions](../engineering/architecture/decisions/decisions.md).

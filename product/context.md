# Identity — Product Context

*Last updated: 2026-10-28*

> Current state — changes often. Durable definition: [product.md](product.md).

## Current state

v0.3 (shared spine) is in progress. v0.1 moved Identity out of the be sdk (2026-10-26); v0.2 built the schema
migrations, signing keys and the OIDC server; v0.3's first iteration made it PostgreSQL only, with keys as vault
secrets, the Data Protection ring in the database, sessions and the sign-in log (2026-10-27). The seven NuGet packages
publish from tags; the npm package still holds the template's sample and is unpublished. The first consumer is Ocharo
(Form 2).

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

- protocol — back-channel logout (I18) and the OIDC client package (I19).
- accounts — invitations, admin-managed members, an OAuth callback-to-link route.
- passwords — expiry, history, forced reset, admin-generated temporary passwords, generator, a real strength meter.
- sessions — a durable OTP store (memory only today, codes plaintext at rest, 5-second resend).
- audit — security events (log lines only today), admin action trail.
- admin — user admin API, disable, impersonation, the console itself.
- Telegram — Log in with Telegram (OIDC), link-on-first-sign-in without a password.
- fe — sign-in, phone + code, Telegram link, passkey, MFA and account screens; `passwordStrength` is empty.

---

## Active tasks

| Task | Status | Notes |
|---|---|---|
| Move the code, tests and research out of the be sdk | ✅ | 2026-10-27; `contract.md` and `package-registry.md` follow from the moved code |
| Move the spec and the analysis batch into the repo | ✅ | 2026-10-26 |
| First npm publish of `@wow-two-beta/fe-identity` | ⬜ | by hand once real fe content lands; then its trusted publisher; tag `fe-v0.0.1` is burned |
| NuGet trusted-publishing consent for the seven ids | ✅ | 2026-10-27; the family published at `10.0.0-beta` |
| Ocharo's secrets and DNS for its instance | ⬜ | by hand; the instance is a service in Ocharo's `deploy.yml` |
| Migrator follow-ups before the first deploy | ✅ | owner marker, baseline and multi-set harness in be sdk `10.0.82-beta` |
| Shortlist Ocharo's needs, close the gaps, deploy with Ocharo | ⬜ | migrations settled 2026-10-28; from the [backlog](../engineering/planning/backlog.md) P1 group |
| Rest of P1, Ocharo fixes, console layout review | ⬜ | after Ocharo's capabilities are verified on its deploy |

---

## Open questions

- Q2 — confirm the registration shape: `Identity:Mode = Module | Oidc`, a shared `IdentityMode` enum, options from
  configuration overridable in code.
- Q4 — the Form 3 host.
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
- 2026-10-26 — D2 widens to one instance per isolation boundary (owner, product or residency); production migrations
  run as an explicit deploy step.
- 2026-10-28 — Ocharo's instance is a service of Ocharo's product, its schema in Ocharo's database; wow2's shared
  instance is a Wheelhouse product of its own. Products may key into Identity's published tables.
- 2026-10-26 — breaking changes are allowed while the products have no users.
- 2026-10-26 — the management screens ship in `@wow-two-beta/fe-identity`'s `./admin` export; `apps/web` composes
  them into the console, and a central services console or a product's admin page imports the same export.
- 2026-10-27 — after the spine, Ocharo's slice comes before wow2's hosted pages: shortlist, close the gaps, deploy
  with Ocharo and verify; the rest of P1 and the console layout review follow.
- every architecture decision with its source: [decisions](../engineering/architecture/decisions/decisions.md).

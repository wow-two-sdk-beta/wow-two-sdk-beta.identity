# Identity — full analysis before the build

*Last updated: 2026-10-26*

> The analysis batch behind `wow-two-sdk-beta.identity` (#2 in the platform app order, `wow-two-ws/docs/platform-apps.md`):
> every integration, the feature vector against the market, the code we already have, how products plug in, the
> package cut and the schema. The [capability spec](../../../product/product.md) is the build source; settled findings
> move into it, and this folder keeps the evidence.

## Inputs

- spec: [product.md](../../../product/product.md) — D1 OpenIddict on our own store, D2 one instance per isolation
  boundary, D3 one person per instance; D5 open in `wow-two-ws/docs/platform-decisions.md`.
- be sdk slices: `engineering/research/identity/identity-slices.md` (moved here from the be sdk) — I1–I13 done,
  I14–I22 planned.
- capability forms: `wow-two-ws/conventions/development/capability-forms.md` — one contract, three forms.
- developer, 2026-10-25: a product reaches a running Identity through an API client behind the broker contract
  (`wow-two-ws/conventions/development/backend/dotnet/core/mla/constructs/behavior/broker.md`).
- developer, 2026-10-25: the SDK splits into one repo per domain — be library, fe library, service (be + fe) and
  Docker together; Identity is the first such repo.
- developer, 2026-10-26: one schema serves every form, so an instance can point at the database a product already
  uses; inventory every table the infrastructure may need, with features switched by configuration (T10).
- developer, 2026-10-26: breaking infrastructure changes are allowed in the SDKs and products whenever they improve
  something, since the products have no users yet; products move to the identity schema without a zero-copy path
  (`wow-two-ws/conventions/development/breaking-changes.md`).

---

## Tracks

| # | Track | File | Wave | State |
|---|---|---|---|---|
| T1 | Third-party integrations: protocol, channels, providers, infra | [t1-integrations.md](t1-integrations.md) | 1 | ✅ |
| T2 | Feature benchmark against the market | [t2-benchmark.md](t2-benchmark.md) | 1 | ✅ |
| T3 | Code reality and consumer inventory | [t3-code-and-consumers.md](t3-code-and-consumers.md) | 1 | ✅ |
| T4 | Integration modes: in-process, API client broker, OIDC sign-in, service tokens | [t4-integration-modes.md](t4-integration-modes.md) | 2 | ✅ |
| T5 | SDK split: Identity as the first per-domain repo | hub: `t5-sdk-breakdown.md` | 2 | ✅ |
| T6 | Hosting, security and conformance for Forms 2 and 3 | [t6-hosting-security.md](t6-hosting-security.md) | 2 | ✅ |
| T7 | Library architecture: abstractions layer, package graph, what the main SDK references | [t7-library-architecture.md](t7-library-architecture.md) | 3 | ✅ |
| T8 | Domain library repo template and the rollout checklist | hub: `t8-library-template.md` | 3 | ✅ |
| T9 | Release automation for domain repos | hub: `t9-release-automation.md` | 3 | ✅ |
| T10 | Full identity schema, feature toggles, migrations and shared databases | [t10-schema-migrations.md](t10-schema-migrations.md) | 4 | ✅ |

- T5, T8 and T9 cover domain repos and release automation in general, so they stay in the hub:
  `wow-two-ws/ideas/identity-analysis-2026-10/`.
- waves 1–2 landed 2026-10-25; waves 3–4 on 2026-10-26.

---

## Points

The decision queue: parents before children, blocking first. Each line: the question → the pick, and the track
holding the evidence. A ticked line carries the developer's answer; the answer moves into the spec.

### A · Placement and shape — blocks the build

- [x] Q1 (developer 2026-10-25: the SDK splits per domain, Identity first) Identity leaves the be sdk for its own repo (be + fe library, service, Docker) as P1 step 0 → yes, no blocker (T5)
- [x] Q1a (developer 2026-10-26: confirmed) repo org: `wow-two-sdk-beta.identity` (T5)
- [x] Q1b (developer: kernel, 2026-10-25) seams: a kernel `WoW2.Sdk.Beta.Be.Abstractions` in the core repo (`ICurrentUserService`, `UserKind`,
  `Result`, `AppError`, `IEvent`); core never references `Identity.Abstractions`, a NuGet cycle → kernel (T7)
- [x] Q1c (developer 2026-10-26: confirmed) seven packages: Abstractions, Auth, Module, Client, Providers, Server, Testing; all of `Identity/` leaves
  core, the auth primitives too (T7 supersedes T5's six)
- [x] Q1d (developer 2026-10-26: confirmed; daily runs wanted whether or not anything changed) versions from tags with
  no release commit; a nightly CI job tests Identity against the latest core release (T5)
- [x] Q1e (developer 2026-10-26: confirmed) fe: `auth` stays in `@wow-two-beta/fe`; `@wow-two-beta/fe-identity` ships
  pages and widgets a consumer embeds, and can instead forward the flow to a running Identity's hosted pages (T7)
- ✗ T5's `…Be.Auth.*` rename in core: moot once the primitives leave with Identity (T7)
- [ ] Q2 (developer 2026-10-26: proposed shape, confirmation pending — a shared `IdentityMode` enum in Abstractions;
  options bound from configuration and overridable in code) one registration, `Identity:Mode = Module | Oidc`, same
  routes, claims, brokers and events → yes (T4)
- [x] Q3 (developer 2026-10-26: confirmed — a self-serving product drops the Module package and adds the Client,
  whose brokers delegate to the running server) Form 1 → 3 move: config plus deleting the module package (T4)
- [ ] Q4 Form 3 host: the control host is off-limits for products → a WoW2 products host, none exists yet (T6)
- [ ] Q5 two owners' instances of one image in Wheelhouse (descriptor keys one slug per repo) → model before Ocharo (T6)
- [x] Q6 [folded into Q54] D2 widens to one instance per owner and per residency, with an onshore option → yes (T6)

### B · Tokens and protocol

- [ ] Q7 browser mode for Forms 2/3: BFF only, SPAs holding tokens forbidden → forbid (T4)
- [ ] Q8 organization scope in tokens: one active organization with switching, not every membership → one (T2, T4)
- [ ] Q9 revocation: short access tokens, offline JWKS checks, custom back-channel logout; no introspection in P1 (T1, T4)
- [ ] Q10 front-channel logout: OpenIddict has no plans → drop from P2 (T1)
- [ ] Q11 management calls: the product checks the acting user in P1; token exchange in P2 → product check (T4)

### C · Sign-in methods — Ocharo P1

- [x] Q12 (developer 2026-10-25) D4: every code channel is configuration — own bot, Telegram Gateway, SMS, email —
  worded by the SDK `IOtpMessageFormatter` (ICU templates, `uz` · `ru` · `en`); Gateway words its own text (T1)
- [ ] Q13 D5: Identity owns the Ocharo bot webhook; others send through the Bot API → yes (spec)
- [ ] Q14 after first link, identity keys on the Telegram user id, never the phone claim → yes (T6)
- [ ] Q15 UZ SMS: Eskiz primary, Twilio and Vonage fallback; Eskiz needs an Uzbek legal entity (T1)
- [ ] Q16 social buttons: Google, Apple, Telegram, Microsoft, Yandex hosted; drop Spotify, Reddit, X; VK → VK ID (T1, T2)
- [ ] Q17 ForeverPin: redirect with a Google hint, or keep one-tap by exchanging the Google ID token (T4)

### D · Feature scope

- [x] Q18 (developer: agreed 2026-10-25) add to P1: test mode with fake channels, operator passkey or TOTP, claims-contributor hook, OTel + health (T2, T6)
- [ ] Q19 add to P1: CSP `frame-ancestors`, no third-party assets on hosted pages, OTP hashed + bound + 30 s resend (T6)
- [x] Q20 (developer 2026-10-25: keep all, configurable) composition, expiry, history, minimum length as policy
  settings; defaults off and minimum 15, per NIST SP 800-63B (T2, T6)
- [ ] Q21 add to P2: bulk import with foreign hashes, token exchange, erasure fan-out, consent records, guest upgrade (T2)
- [ ] Q22 add to P2: Telegram Mini App `initData`, phone and email change recovery, declarative config, paid-channel guard (T2)
- [ ] Q23 move to P3 or drop: SAML, SCIM, device flow, impossible travel, WhatsApp, account merge → link only (T2)
- [ ] Q24 hash-chained trail → events into the audit capability; charts → OTel + Grafana (T2)

### E · Security and operations

- [ ] Q25 signing keys: Wheelhouse-rendered host files as X.509 with staged `NotBefore`; I17 = key-source seam (T6)
- [ ] Q26 Data Protection ring in PostgreSQL, certificate-protected, ASP.NET ≥ 10.0.7 (T1, T6)
- [ ] Q27 counsel reading of the telecom-user clause before a UZ-audience product joins Form 3 (T6)
- [ ] Q28 conformance suite in CI with a `Testing` host mode; certification deferred (T6)
- [ ] Q29 D13 email: SES, already wired and cheapest (T1)
- [ ] Q30 domain binding and off-host backups (both planned in Wheelhouse) gate Ocharo's cutover (T6)

### F · Migration

- [x] Q31 [folded into Q47] one migrator for identity tables across Form 1 hosts and the service before I14–I16 (T3)
- [x] Q32 [folded into Q55] Ocharo: Desk keeps ASP.NET Core Identity tables with text ids → rename + key-type plan; port Desk's prototypes (T3)
- [x] Q33 [folded into Q56] Haven's several phones per user vs one `PhoneNumber` → settle the model before Haven moves (T3)
- [ ] Q34 Wheelhouse stays Form 1: it deploys Identity and must sign in without it → yes (T3)

### G · Library repo and template · H · Release automation

- Q35–Q45 belong to T8 and T9 and stay in the hub index: `wow-two-ws/ideas/identity-analysis-2026-10/identity-analysis-2026-10.md`.

### I · Schema and migrations (T10)

- [x] Q46 (developer 2026-10-26: confirmed) migration strategy: A whole schema always · B on demand · C hybrid → A; configuration switches reads and writes only (T10)
- [x] Q47 (developer 2026-10-26: confirmed) schema authority: a SQL set in `Identity.Module`, run by the be sdk migrator, EF only maps · EF migrations per consumer → SQL set (answers Q31)
- [x] Q48 (developer 2026-10-26: confirmed) migrator: named sets per host, `CREATE SCHEMA`, a `{{schema}}` placeholder, startup level check and owner marker, before I14–I16 → yes
- [x] Q49 (developer 2026-10-26: confirmed) keys: `uuid` only, Guid v7 made in code; `TKey`/`TUser` generics dropped from stored entities → yes
- [x] Q50 (developer 2026-10-26: confirmed) extension: side tables keyed by `sub` plus claims contributors; `attributes jsonb` the narrow exception; `TUser` columns retired → yes
- [x] Q51 (developer 2026-10-26: confirmed) feature switches: configuration only in both forms, the Module registers every slice · code `.AddX()` decides in Form 1 → configuration (reverses 2026-09-28)
- [x] Q52 (developer 2026-10-26: confirmed) naming: schema `identity` plus the `identity_` prefix · bare names · prefix only → schema plus prefix
- [x] Q53 (developer 2026-10-26: confirmed) shared database: never one identity schema for two instances; a database per instance by default, a schema per instance allowed → yes
- [x] Q54 (developer 2026-10-26: confirmed) D2: one instance per isolation boundary (owner, product or residency); capability forms § Ownership becomes per schema → yes (folds Q6)
- [x] Q55 (developer 2026-10-26: confirmed) Ocharo: its own database with a one-time copy of Desk's converted account tables · take over Desk's database → own database (folds Q32)
- [x] Q56 (developer 2026-10-26: confirmed) Haven: `identity_user_phones`, several per person, one primary; `users.phone_number` retires → yes (folds Q33)
- [x] Q57 (developer 2026-10-26: confirmed) production migrations: an explicit `migrate` deploy step · apply on boot → explicit step, boot in development only
- [x] Q58 (developer 2026-10-26: confirmed) SQLite: a supported Form 1 dialect, pilots copy to PostgreSQL at launch · test-only → supported

### Self-served

- ✅ spec facts corrected from T3: 113 tests, I13 shipped, OTP defaults, Desk session store, OpenIddict support
- ⬜ build-step facts, no decision: OpenIddict 6.0.0 → 7.7.2 with EF Core ≥ 10.0.11; Fido2 3.0.1 → 4.3.1 (security)
- ⬜ claim vocabulary converges on `sub`, `sid`, `org_id` before an OIDC client ships (T4)
- ✅ Q48 migrator shipped in be sdk `10.0.81-beta`: named sets, `CREATE SCHEMA`, `{{schema}}`, level check, owner marker (T10)
- ⬜ migrator follow-ups before Identity's first deploy: `MigrateBespokeOnStartupAsync` applies at every boot and one
  platform host calls it in production (against Q57); the owner marker must name the instance, not `module`/`service`;
  no `BaselineAsync`; `MigratorHarness` in `Testing.Data` builds one set only; `wow-migrate` connects to PostgreSQL only
- ✅ split prerequisites shipped in be sdk `10.0.77-beta`, 2026-10-26: kernel `WoW2.Sdk.Beta.Be.Abstractions` with
  forwarders, `AddErrorTexts` seam, `IdentityApi/` and `Policies/` deleted (T5, T7)

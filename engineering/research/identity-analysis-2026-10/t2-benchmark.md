# T2 — Identity feature benchmark

*Last updated: 2026-10-26*

> The planned `wow-two-sdk-beta.identity` vector against 12 mature products: what the plan misses, what it over-scopes,
> and how the library, self-host and cloud products keep one contract. A feature benchmark, not a build-vs-buy re-run.

## Matrix

- Spec = the capability spec, now [product.md](../../../product/product.md) and its [features](../../../product/features/features.md); phases P1 / P2 / P3; `exists` = already in be SDK `Identity/`.
- Products: **KC** Keycloak 26.8 · **ZIT** Zitadel v4 · **LG** Logto 1.41 · **ORY** Kratos / Hydra / Keto · **AK** authentik · **FA** FusionAuth · **A0** Auth0 · **CL** Clerk · **WO** WorkOS · **ST** Stytch (Twilio-owned) · **SB** Supabase Auth · **BA** Better Auth 1.7.
- Unmarked = confirmed in a vendor doc, changelog or release note fetched 2026-10-25.
- `(U)` = unverified: prior knowledge, a third-party summary, or a vendor page that did not state it.
- `(V)` = rests on a vendor comparison or blog page, so vendor-reported.

### Sign-in methods

| Feature | Our spec | Who has it |
|---|---|---|
| Passkeys, usernameless, autofill UI | P2 (Fido2 exists) | KC 26.4 (conditional + modal UI), LG 1.38 (autofill), BA, WO, AK (WebAuthn stage), FA; A0, CL, ST, ZIT, ORY (U) |
| Email magic link | P2 (exists) | BA, SB, WO (Magic Auth), ST; A0, CL (U) |
| Email or phone one-time code | P2 (exists) | BA (email OTP + phone plugins), SB, LG, ORY (code method), WO |
| Telegram OIDC login | P1 | Via generic OIDC: KC, AK (named in Telegram's docs), SB (self-host custom OIDC guide); BA community plugin `better-auth-telegram` (unofficial); LG community connector (unofficial) |
| Telegram Mini App `initData` sign-in | missing | BA community plugin only (U) |
| Phone + code via chat-app bot | P1 | none native; built from OTP-callback seams (BA `sendOTP`, SB Send SMS hook, A0 `send-phone-message`) |
| Telegram Gateway codes | P2 | none found |
| SMS / WhatsApp OTP, own provider | P2 | SB hook, A0 action, BA callback, AK SMS stage, LG (WhatsApp connector, v1.39) |
| Email + password | P2 | all 12 |
| Social login | P2 (17 + generic OIDC) | BA (about 35 providers), SB (about 19 + custom OIDC); the rest ship a smaller list plus generic OIDC |
| Enterprise SSO, SAML / OIDC | P3 | KC, ZIT, LG, AK, FA, A0, CL (SAML, OIDC, EASIE), WO, ST, SB (SAML), BA; ORY only on Ory Network (U) |
| Device flow (RFC 8628) | P2 | OpenIddict native; ZIT, LG 1.38, BA 1.7; KC, ORY Hydra, A0, AK (U) |
| Client credentials / machine identity | P2 | BA (fail-closed scopes), FA (entities), A0 (`credentials-exchange` trigger); others (U) |
| Personal access tokens / API keys | P2 (API keys exist) | BA API-key plugin; others (U) |
| Anonymous / guest → upgrade | missing | SB (same user id, `is_anonymous` claim), BA (`onLinkAccount` callback) |
| Chat approve button as second factor | P2 | none; nearest is AK Duo stage (U) |
| Multi-session / account switcher | missing | BA; CL (U) |

### Accounts and B2B organizations

| Feature | Our spec | Who has it |
|---|---|---|
| Organizations, memberships, roles | P1 | KC (since 26.0), ZIT, LG, A0, CL, WO, ST, BA; SB lacks first-class orgs (V); ORY orgs on Network only (U) |
| Roles defined once per app, assigned per org | P1 (owner / editor / viewer + permissions) | LG organization template, CL application-level roles |
| Custom roles at runtime | missing | BA dynamic roles (per org, capped), CL custom roles, ST custom RBAC |
| Invitations, expiry, cancel | P1 (admin invite) | BA (2-day default), KC (links), LG, CL, A0, ST, WO |
| Domain verification (DNS TXT) | missing | BA SSO plugin (token valid 1 week), WO Admin Portal, A0 verified org domains; KC keeps a `verified` field but does no DNS check (V) |
| Auto-join by email domain (JIT) | missing | LG (JIT email domains), CL (auto-invite or suggest), ST (`email_jit_provisioning`), A0 (JIT via connection), BA (needs verified domain), KC (domain matching) |
| Per-org SSO connection | P3 | BA, KC (IdPs shared across orgs, 26.8), LG, A0, CL (Enhanced B2B add-on), WO, ST |
| Self-service SSO setup for the customer's IT admin | missing | WO Admin Portal, A0 My Organization components, CL (June 2026) |
| Inbound SCIM provisioning | P3 | KC (supported 26.8), BA, CL (GA Apr 2026), WO, ST; AK, ZIT (U) |
| Org in tokens / org switching | P1 (claims) | CL active org in session token (4 KB cap), ST session exchange per org, LG org-scoped tokens, A0 org-scoped, BA `setActive` |
| Account linking | P2 | SB manual linking, BA (auto-link on verified domain), KC, A0 (U) |
| Account merge | P2 | none document a generic merge (U); SB and BA link credentials and leave data to the app |
| Extensible profile schema | fixed fields | KC user profile, BA `additionalFields`, LG custom data, ORY identity schemas (U), CL metadata (U) |
| Ban / suspend with expiry | P1 disable | BA (ban + expiry, revokes sessions), LG (suspend revokes refresh tokens) |

### Authorization

| Feature | Our spec | Who has it |
|---|---|---|
| RBAC, roles in tokens | P1 | all 12 |
| Permissions in tokens | P1 | BA, ST (implicit roles by SSO / SCIM / email domain), SB (via hook), KC mappers |
| Relationship-based (Zanzibar) | missing | ORY Keto (OPL), WO FGA via Warrant (maturity U), A0 FGA / OpenFGA (U), FA fine-grained (U) |
| Step-up, `max_age`, ACR levels | P2 | KC (ACR → level of authentication, SAML step-up), BA 1.7 (`max_age` enforced) |
| Required MFA per role | P2 | KC, ZIT (U); ST per-org policy (U) |
| Token exchange (RFC 8693) | missing | OpenIddict native; KC (standard exchange 26.2, delegation preview 26.8), ZIT (impersonation), A0 (custom token exchange trigger) |
| Org-scoped machine tokens | missing | A0 (M2M on behalf of an org), FA entities |
| Agent / MCP authorization | missing | KC 26.5, BA (`@better-auth/mcp`, CIMD), SB OAuth 2.1 server, FA entities as agents; ZIT roadmap H2 2026 |

### Sessions and tokens

| Feature | Our spec | Who has it |
|---|---|---|
| Code + PKCE, rotating refresh, reuse revokes family | P1 (refresh exists) | BA, KC, others (U) |
| Asymmetric signing keys, rotation | P1 | SB (RS256 default, ES / Ed25519 optional), KC realm keys, LG (safer rotation, 1.39), BA (client key rotation) |
| Session list, revoke one or all | P1 | KC, BA (admin + multi-session), LG 1.38 (sessions + grants), SB, CL (U) |
| Per-app token lifetimes | P1 (config) | BA 1.7 (per-API rules), KC, A0 (U) |
| Token customization hook | missing | A0 `post-login`, ZIT Actions v2, LG custom JWT script, SB custom access token hook, BA `customAccessTokenClaims`, FA lambda, KC mappers, CL JWT templates + session claims |
| Back-channel logout | P1 | BA 1.7, KC (U); OpenIddict has no native support yet (8.0 preview 3 adds session support "for a future preview") |
| Front-channel logout | P2 | KC (U) |
| Impersonation with `act` claim | P3 | KC 26.8 (`act` + event), ZIT (token exchange + 4 impersonator roles), CL actor tokens (30 min default, revocable), BA admin (1 h default), AK, LG |
| Sender-constrained tokens (DPoP) | missing | BA 1.7, KC 26.4; OpenIddict (U) |
| Dynamic client registration | missing | BA (initial access token), SB OAuth server (U) |
| Consent screen + stored grants | P3 | BA (per `consentReferenceId`), LG grant management, KC |

### Extensibility

| Feature | Our spec | Who has it |
|---|---|---|
| Outbound webhooks | P2 | LG, ZIT (event targets), FA, KC (listener SPI); CL, ST, WO (U) |
| Synchronous hooks that change flow or token | missing | A0 (8 triggers), ZIT Actions v2 (request / response / function / event), SB (6 hooks), FA lambdas, LG Actions, BA hooks, KC Java SPI, ORY flow hooks |
| Pre-registration validation | missing | A0 `pre-user-registration`, SB Before User Created, BA `user.validateUserInfo` |
| Custom OTP / email sender | exists (adapters) | SB hooks, A0 `send-phone-message`, BA callbacks |
| Flow designer / custom stages | none | AK (25 stage types), KC authentication flows |
| Event stream to SIEM | P2 (hash-chained trail) | ZIT event API + SIEM streaming, A0 log streams (at-least-once, 3 attempts), KC listener, WO audit-log streaming (paid) |

### Admin console

| Feature | Our spec | Who has it |
|---|---|---|
| User search, filter, sort, paging | P1 | all 12; BA admin plugin |
| Bulk import / export | missing | LG (Management API, rate-limited), ORY batch import (U), BA migration scripts, FA Import API (U), KC partial import (U) |
| Sign-in log, admin action log | P1 log / P2 trail | ZIT event store (append-only), LG audit logs (time-range picker, 1.40), KC events (off by default) + admin events, WO Audit Logs (paid), BA Infrastructure audit logs (hosted, paid) |
| Overview analytics | P1 counts / P2 charts | BA `dash()` (hosted, paid); ZIT lists analytics for v5 (roadmap); LG, CL (U) |
| Impersonation | P3 | CL, ZIT, KC, AK, BA, LG |
| Delegated admin for org owners | missing | KC (org roles + FGAP, 26.7), ZIT org-owner roles, A0 My Organization, WO Admin Portal |
| Restrict console to operators | implied | ZIT (can block end-user console access) |

### Developer surface

| Feature | Our spec | Who has it |
|---|---|---|
| SDKs per stack | .NET + Vue only | all vendors ship 10+ stacks; BA lists 20+ framework integrations |
| Management API | P2 (scoped keys) | LG Management API, CL Backend API, ZIT resource API v2, KC Admin REST (v2 for clients, preview) |
| CLI | missing | BA (`migrate`, `generate`, `check schema`, `create-admin`), ORY (config export / import), ZIT roadmap "CLI-first"; A0, LG, SB (U) |
| Declarative config / IaC | P1 clients by config | AK blueprints (YAML) + Terraform / Pulumi, ZIT Terraform + "identity-as-code" roadmap, ORY CLI + Terraform, KC Terraform provider, FA Pulumi; KC realm import, FA Kickstart (U) |
| Test / dev mode | missing | CL development instances + Testing Tokens, BA Test Utils plugin; ZIT roadmap "local identity runtime"; WO sandbox, ST test env, SB local stack (U) |
| Embeddable components vs redirect | hosted pages + fe SDK kit | CL embedded components, A0 My Organization components (+ Universal Login redirect), LG redirect + account center app (1.37), ORY headless API flows, BA headless client |
| OpenAPI / typed client | n/a | BA Open API plugin |

### Hosted UI

| Feature | Our spec | Who has it |
|---|---|---|
| Per-app branding | P1 (theme per client) | A0 per-org branding, ZIT console branding, AK themes, FA theme editor, KC themes (U) |
| Localization | P1 (`uz`, `ru`, `en`) | KC i18n, BA i18n plugin, ZIT custom login texts; LG, A0, CL (U); Uzbek coverage unverified everywhere |
| Custom domains | missing | A0 (CLI), ZIT, LG (redirect fix, 1.37), WO (paid add-on, V); CL satellite domains (U) |
| Self-service account center | P1 sessions + recent; P2 rest | LG account center app, AK user interface, ZIT console self-service, KC account console (U) |
| Identifier-first page | phone first | A0, LG |

### Security

| Feature | Our spec | Who has it |
|---|---|---|
| Breached-password check | exists (fails open) | A0, FA, BA (HIBP plugin); KC, LG, AK, SB, CL (U) |
| Rate limits, brute-force lockout | P1 / P2 progressive | A0 (brute force + suspicious-IP throttle), KC brute-force detection, BA rate limit, FA rate limiting |
| Bot detection / CAPTCHA | missing | A0 (IP reputation → CAPTCHA), CL (Turnstile, Business plan), BA captcha plugin, SB, WO Radar, LG (captcha rules, 1.41), AK stage, KC reCAPTCHA |
| SMS-pumping protection | P1 limits / P2 country list | ST (new projects: US / Canada only by default), WO Radar SMS challenge; CL, SB, A0 (U) |
| Impossible travel, device fingerprint | P3 | WO Radar, FA advanced threat detection, BA sentinel (paid, contents unconfirmed) |
| Adaptive / risk-based MFA | P3 | LG 1.38 (release note only, U), A0 (U) |
| New-device alert | P2 | WO Radar unrecognized-device notification |
| Trusted device (skip MFA) | P2 (remembered devices) | LG trusted devices (opt-in, revocable) |
| Account lockdown | "sign out everywhere" | AK Account Lockdown stage |
| Shared Signals (SSF / CAEP) | missing | KC (experimental, 26.7), AK SSF provider |

### Compliance

| Feature | Our spec | Who has it |
|---|---|---|
| Personal-data export | exists | none document a built-in subject export (U) |
| Delete / erase | P1 | all; KC admin events can hold personal data when "include representation" is on |
| Erasure fan-out to apps | missing | event-driven: LG webhooks, ZIT events, WO events (U) |
| Consent records (ToS, privacy) | missing | CL legal-acceptance checkbox (boolean; no timestamp documented), BA OAuth consent store; KC, LG, A0 terms (U) |
| Data residency | missing | ZIT roadmap "regional deployment" (H2 2026); A0, CL, WO, LG Cloud regions (U) |
| Uzbek data-localization law | missing | amended 2026-03-26: most data may go offshore on conditions; telecom-user and biometric data stay in-country (statute text unread, U) |

### Migration

| Feature | Our spec | Who has it |
|---|---|---|
| Import with foreign hashes | missing (Desk move only) | LG (MD5, SHA1, SHA256, bcrypt; rehash to Argon2 on first sign-in; `Legacy` digest for others), ORY (verify + re-hash; sha-crypt, md5-crypt, HMAC, Argon2), BA (bcrypt copy, custom hash / verify) |
| Keep user ids and external subjects | missing | LG (custom id, self-hosted only), BA (Auth0 ids kept; each identity becomes an account row) |
| Lazy / trickle migration | missing | LG post-first-factor Action, FA Connectors (paid), ORY Network password-migration hook |
| Migrate-from guides | n/a | BA (Auth.js, Auth0, Clerk, Supabase, WorkOS) |

### Operations

| Feature | Our spec | Who has it |
|---|---|---|
| Multi-instance, durable stores | P1 (PostgreSQL) | AK (PostgreSQL only since 2025.10), KC (multi-cluster v2, 26.8), BA (optional Redis secondary storage + atomic adapter ops), LG |
| Metrics, health, tracing | missing | BA (Instrumentation, experimental); KC, AK, ZIT (U) |
| Backups | not stated | DB-level in every product; no identity-specific feature |
| Zero-downtime upgrades | not stated | ORY Network lists it as a hosted advantage over self-host |
| Version support window | not stated | ZIT: V2 end-of-life, each major supported 12+ months, new major every 3 months |
| First-admin bootstrap | not stated | BA `create-admin`; LG OSS needs a self-hosted license for more than one admin |

### Delivery forms

| Product | Library in-process | Self-host service | Cloud | One contract across forms |
|---|---|---|---|---|
| BA | yes (TypeScript, server + client plugin pairs) | your app is the host | Infrastructure: `dash()`, `sentinel()`, email / SMS plugins + API key | yes: plugins add a control plane, core API unchanged |
| ORY | no | Kratos, Hydra, Keto as separate services | Ory Network, same codebase | mostly; B2B and some features Network-only (U) |
| SB | no | Auth server (Go) in Docker | Supabase Cloud | same REST API + `supabase-js`; self-hosters follow the Auth changelog for fixes |
| KC, AK, ZIT, LG, FA | no | server image | LG, ZIT, FA vendor clouds; AK enterprise; KC none | OIDC / SAML + admin REST; editions gate features (LG admin seats, FA connectors, AK enterprise) |
| A0, CL, WO, ST | no | no | cloud only | vendor SDK per stack |

---

## Missed features, ranked

- **Test / dev mode and fake channels → P1** — phone + bot-code sign-in cannot run in any consumer app's E2E suite without it; CL Testing Tokens, BA Test Utils, ZIT "local identity runtime" fill the same gap. Ship an in-memory code sink, a dev-only persona sign-in and a test-host helper in the SDK Testing harness.
- **Second factor for console operators → P1** — phone + Telegram code alone guards the highest-value account; KC 26.4 and LG 1.38 treat passkeys as mainstream, and Fido2 and TOTP already exist in the SDK.
- **Claims-contributor hook (and sync hooks later) → P1 for the claims seam, P2 for the rest** — all 12 products let code add token claims or veto sign-up; Form 1 needs a DI interface, Forms 2 and 3 a webhook target; roles-only claims force an identity release per app request.
- **Paid-channel guard → P2, shipped with the first paid channel** — default-deny country list (start `UZ` only, like ST's US / Canada default), a daily spend cap per channel, and CAPTCHA on unauthenticated code requests; Gateway costs $0.01 per code and SMS costs more.
- **Anonymous / guest → upgrade → P2** — SB and BA keep the same user id and add a claim; consumer apps (Arcade, Hijinx, Pose Coach) benefit from try-before-sign-up.
- **Bulk import with foreign hashes, external-subject mapping and id preservation → P2, before Form 3** — ForeverPin carries Google subjects, the pilots carry email + password, Haven carries Telegram links; LG and ORY show the hash and rehash pattern. Lazy migration → skip: every legacy store is ours.
- **Telegram Mini App `initData` sign-in → P2** — Arcade is a Telegram Mini App; only an unofficial BA plugin ships it; cost is one HMAC check with an `auth_date` window.
- **Erasure fan-out and consent records → P2** — emit `user.deleted` and `member.removed` to apps; store ToS / privacy version + timestamp at sign-up (CL stores only a boolean).
- **Data-localization decision for Forms 2 and 3 → P1 decision, not a feature** — the 2026 Uzbek amendment keeps telecom-user data in-country; phone numbers of Uzbek mobile users may fall under it (U: confirm against the statute).
- **Metrics, health and tracing → P1** — OpenTelemetry meters and `/health` for the image; cheap in .NET, needed before Form 3 runs unattended.
- **Declarative config file → P2** — one YAML for clients, roles, org templates and policies, plus `export` for Form 2 → Form 3 moves; AK blueprints and ZIT "identity-as-code" are the model. Terraform provider → skip.
- **Token exchange (RFC 8693) → P2** — OpenIddict supports it; covers service-to-service calls, org switching without re-login, and impersonation with an `act` claim.
- **Phone / email change and lost-phone recovery → P2** — number recycling makes a phone a weak identifier; BA verifies the new number by OTP, KC has an update-email workflow; require a second factor or admin step.
- **Delegated org admin → P3** — org owners manage their own members (A0 My Organization, WO Admin Portal); needed once an org has more than one operator.
- **Domain verification + JIT by email domain → P3** — ships with per-org SSO; verify by DNS before any auto-join (BA requires it; KC does not check DNS (V)).
- **Dynamic client registration, CIMD, DPoP, agent identities → skip** — first-party clients come from config; revisit when a named MCP or high-risk consumer appears.
- **Multi-session / account switcher, last-used-method hint → P3** — BA and CL ship both; UX polish.

---

## Possible over-scope

- **Password expiry, history, composition rules (P2)** — NIST SP 800-63B advises against forced rotation; LG added expiry only in 1.41 (Jun 2026); workforce-oriented products carry the rest. Keep min length, breached check and the meter.
- **Password policy scoped per client and per organization (P2)** — three scopes multiply UI and tests; instance + client is enough.
- **Password generator in the field, admin-issued temporary passwords (P2)** — no sampled product documents an in-field generator (U); browsers and password managers cover it. Keep a temporary password only if an operator account uses passwords.
- **Account merge (P2)** — SB and BA link credentials and leave data to the app (`onLinkAccount`); a person merge forces every app to remap `sub`. Offer link + admin-run offline merge.
- **Hash-chained admin trail (P2)** — ZIT's event store is append-only, none documents hash chaining. Emit identity events into the platform audit capability; keep a plain append-only table here.
- **Overview charts (P2)** — ZIT lists analytics for v5 and BA sells `dash()` as paid infra; ship counts + CSV, chart from OpenTelemetry metrics in Grafana.
- **SAML 2.0 and SCIM (P3)** — SAML adds an XML-signature attack surface (WO's 2026 blog lists XXE and signature-injection patching as a burden, V); Entra ID and Google Workspace both speak OIDC, which CL's EASIE uses. Defer SAML to a named customer; consider a SAML broker over building one.
- **Impossible travel and risk scoring (P3)** — only WO Radar, FA threat detection and paid BA sentinel offer it; the new-device alert (P2) gives most of the value.
- **Device flow and PATs (P2)** — OpenIddict supplies device flow, but LG and BA added it only in 2026; move to P3 until a CLI consumer exists. API keys already cover PATs.
- **WhatsApp adapter (P2)** — LG added WhatsApp in v1.39 (Apr 2026); with Telegram bot, Gateway and Eskiz SMS in the plan, start with Gateway + Eskiz.
- **All 17 social registrations as hosted buttons (P2)** — BA ships about 35 as config; the cost is per-provider testing. Enable Google, Apple, Telegram, Microsoft, Yandex; leave the rest SDK-only.
- **Back-channel logout at P1 (cost flag, keep)** — OpenIddict has no native support (8.0 preview 3 adds session support for "a future preview"); BA 1.7 and the WO article show fan-out needs retries and audit. Reuse the SDK's EF outbox for delivery.
- **Conformance suite before Form 2 (keep)** — OpenIddict is not submitted for certification; ORY Hydra is certified, KC (U). Scope the suite to the profiles used.

---

## Delivery-form lessons

- **BA shows Form 1 done well** — server + client plugin pairs, the consumer owns tables, a CLI generates or runs migrations, schema checked at startup. Matches `capability-forms.md` § Ownership.
- **BA's hosted form adds, never forks** — `dash()`, `sentinel()`, audit logs, email and SMS arrive as plugins plus an API key; the core contract stays identical. Model for Form 3.
- **ORY and SB ship one codebase for self-host and cloud** — same API, same client; gaps appear as Network-only features (U). Keep the image and Form 3 on one build, and publish a feature-parity line.
- **ORY separates public and admin APIs** — end-user flows and management live on different surfaces. Mirror that: flow API for the sign-in kit, management API with scoped keys.
- **CL splits Frontend and Backend APIs and verifies JWTs offline** — apps check tokens against cached JWKS, so a Form 3 outage does not stop every request. Make offline verification the default in the OIDC client module.
- **Hydra delegates login and consent to the app** — a pattern for Form 1 hosts that keep their own sign-in UI while using the OIDC server module.
- **Editions gate features in server products** — LG admin seats, FA connectors and entities, AK enterprise, ORY Network B2B. We have no editions, so features must differ by configuration, never by form.
- **The Form 1 contract is not OIDC** — BA serves cookie sessions in-process and offers OIDC through a separate provider plugin. Define the contract as the claims principal, management API and events; the protocol exists only in Forms 2 and 3.
- **Breaking data migrations hurt module hosts** — BA 1.7 moved identity to `(issuer, accountId)` and needed a maintenance window (V, WO write-up). Ship additive migrations, expand then contract, and a version-support policy (ZIT: 12 months per major).
- **Org-in-token needs a size policy** — CL caps session tokens at 4 KB; LG and A0 issue org-scoped tokens. Carry one active org per token and switch by exchange, not a list of all memberships.
- **Keep ReBAC outside identity** — Keto, WO FGA and A0 FGA are separate products. Expose roles and permissions in claims and a seam for an external evaluator.
- **Two registrations behind one contract double test surface** — only BA offers in-process; the conformance suite must run against both the module and the image, as the forms doc requires.

---

## Sources

- [Better Auth docs index](https://better-auth.com/docs/llms.txt) · [organization](https://better-auth.com/docs/plugins/organization) · [SSO](https://better-auth.com/docs/plugins/sso) · [admin](https://better-auth.com/docs/plugins/admin) · [OAuth provider](https://better-auth.com/docs/plugins/oauth-provider) · [SCIM](https://better-auth.com/docs/plugins/scim) · [phone number](https://better-auth.com/docs/plugins/phone-number) · [anonymous](https://better-auth.com/docs/plugins/anonymous) · [database](https://better-auth.com/docs/concepts/database) · [1.7 release](https://better-auth.com/blog/1-7) · [Auth0 migration](https://better-auth.com/docs/guides/auth0-migration-guide) · [Infrastructure](https://better-auth.com/docs/infrastructure/getting-started)
- [better-auth-telegram (community)](https://github.com/vcode-sh/better-auth-telegram)
- [Keycloak release notes](https://www.keycloak.org/docs/latest/release_notes/) · [Server administration guide](https://www.keycloak.org/docs/latest/server_admin/index.html) · [Red Hat build 26.4 notes](https://docs.redhat.com/en/documentation/red_hat_build_of_keycloak/26.4/html-single/release_notes) · [Skycloak on organization domains](https://skycloak.io/blog/home-realm-discovery-keycloak-organizations/) (V)
- [Zitadel V4 GA](https://zitadel.com/blog/announcing-the-general-availability-of-zitadel-v4) · [roadmap](https://zitadel.com/docs/product/roadmap) · [Actions v2](https://zitadel.com/docs/concepts/features/actions_v2) · [impersonation](https://zitadel.com/docs/guides/integrate/token-exchange) · [audit trail](https://zitadel.com/docs/concepts/features/audit-trail) · [console](https://zitadel.com/docs/guides/manage/console/overview) · [2023 recap](https://zitadel.com/blog/recap-2023)
- [Logto introduction](https://docs.logto.io/introduction) · [organizations](https://docs.logto.io/organizations) · [JIT provisioning](https://docs.logto.io/organizations/just-in-time-provisioning) · [custom token claims](https://docs.logto.io/developers/custom-token-claims) · [user data](https://docs.logto.io/user-management/user-data) · [user migration](https://docs.logto.io/user-management/user-migration) · [MFA](https://docs.logto.io/end-user-flows/mfa) · [sign-in methods](https://docs.logto.io/end-user-flows/sign-up-and-sign-in) · [OSS](https://docs.logto.io/logto-oss/get-started-with-oss) · [release summaries](https://blog.logto.io/zh-CN)
- [Ory Kratos self-service](https://www.ory.com/docs/kratos/self-service) · [Keto](https://www.ory.com/docs/keto) · [Hydra](https://www.ory.com/docs/hydra) · [import identities](https://www.ory.com/docs/kratos/manage-identities/import-user-accounts-identities) · [Ory Network vs self-hosted](https://www.ory.com/blog/ory-network-or-self-hosting-explained-use-cases)
- [authentik docs](https://docs.goauthentik.io/) · [stages](https://docs.goauthentik.io/add-secure-apps/flows-stages/stages/) · [We removed Redis](https://goauthentik.io/blog/2025-11-13-we-removed-redis/) · [Pulumi / Terraform provider](https://www.pulumi.com/registry/packages/authentik/api-docs)
- [FusionAuth docs](https://fusionauth.io/docs/) · [entity management](https://fusionauth.io/docs/get-started/core-concepts/entity-management) · [community: connectors](https://fusionauth.nodebb.com/community/forum/post/3514)
- [Auth0 Actions triggers](https://auth0.com/docs/customize/actions/explore-triggers) · [attack protection](https://auth0.com/docs/secure/attack-protection) · [organizations](https://auth0.com/docs/manage-users/organizations) · [log streams](https://auth0.com/docs/customize/log-streams)
- [Clerk organizations](https://clerk.com/docs/guides/organizations/overview) · [bot protection](https://clerk.com/docs/guides/secure/bot-protection) · [impersonation](https://clerk.com/docs/guides/users/impersonation) · [legal compliance](https://clerk.com/docs/guides/secure/legal-compliance) · [JWT templates](https://clerk.com/docs/guides/sessions/jwt-templates) · [SSO + SCIM article](https://clerk.com/articles/auth-platforms-with-sso-and-scim-in-the-base-tier) (V)
- [WorkOS Radar](https://workos.com/docs/radar) · [WorkOS docs (llms-full)](https://workos.com/llms-full.txt) · [running your own OIDC provider, 2026](https://workos.com/blog/problems-with-running-your-own-oauth-oidc-provider-2026) (V) · [pricing at scale](https://ssojet.com/blog/workos-pricing-at-scale-50-100-200-connections) (V)
- [Stytch JIT provisioning](https://stytch.com/docs/b2b/guides/organizations/jit-provisioning) · [enterprise-ready guide](https://stytch.com/docs/get-started/guides/enterprise-ready) · [toll fraud](https://stytch.com/docs/resources/policies/messaging/toll-fraud) · [changelog](https://stytch.com/docs/resources/changelog)
- [Supabase Auth](https://supabase.com/docs/guides/auth) · [auth hooks](https://supabase.com/docs/guides/auth/auth-hooks) · [anonymous sign-ins](https://supabase.com/docs/guides/auth/auth-anonymous) · [OAuth 2.1 server](https://supabase.com/docs/guides/auth/oauth-server) · [supabase/auth](https://github.com/supabase/auth)
- [OpenIddict README](https://raw.githubusercontent.com/openiddict/openiddict-core/dev/README.md) · [OpenIddict releases](https://github.com/openiddict/openiddict-core/releases)
- [Log In With Telegram](https://core.telegram.org/bots/telegram-login) · [Telegram Gateway](https://core.telegram.org/gateway) · [Twilio: preventing toll fraud](https://www.twilio.com/docs/verify/preventing-toll-fraud)
- [Uzbekistan 2026 personal-data amendment (Kun.uz)](https://kun.uz/en/news/2026/03/27/uzbekistan-amends-personal-data-law-to-facilitate-global-payment-systems) (V) · [Legal 500 overview](https://www.legal500.com/developments/thought-leadership/personal-data-compliance-in-uzbekistan/) (V)

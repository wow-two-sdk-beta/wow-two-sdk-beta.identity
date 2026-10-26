# T6 — Identity hosting, security and conformance

*Last updated: 2026-10-26*

> How Forms 2 and 3 of `wow-two-sdk-beta.identity` are hosted, secured and proven correct: the Uzbek data law read at
> the source, topology on Wheelhouse, keys, a threat model, ASVS / NIST / RFC 9700 baselines, OIDF conformance and operations.

- Legend: `(U)` unverified: prior knowledge, a summary of a page, or a source that did not state it. `(V)` vendor or marketing page.
- Unmarked = read on 2026-10-25 in the source listed under Sources. Statute text came through a page summary of lex.uz, so the official Uzbek text stays unread.
- Builds on [T2](t2-benchmark.md): OpenIddict has no native back-channel logout; test mode with fake channels is P1; the Uzbek amendment was flagged `(U)`.

---

## 1. Data localization

### The law as amended

| Fact | Value |
|---|---|
| Base law | ЗРУ-547 «О персональных данных», 2 July 2019, in force 1 Oct 2019 |
| Amending law | ЗРУ-1125, signed 26 Mar 2026; Legislative Chamber 20 Jan, Senate 5 Feb |
| In force | on official publication, 27 Mar 2026; no transition period, no deadline in the text |
| Art. 27¹ | replaced in full |
| Art. 27¹ part 1 | citizens' data is stored while the law's basic principles hold |
| Art. 27¹ part 2 | mandatory storage in Uzbekistan: biometric data, genetic data, «данные физических лиц — пользователей услуг операторов телекоммуникаций, осуществляющих свою деятельность на территории Республики Узбекистан» |
| Art. 27¹ part 3 | all other data may be stored and processed abroad if one holds: adequate-protection state; operator's SCC or BCR approved by the authority; compliance with international standards from a list the authority approves |
| Art. 20 part 1 | only databases under part 2 must be registered in the State Register of personal-data databases |
| Art. 15 | cross-border transfer: not amended; a non-adequate destination needs the subject's consent (itts.uz, Legal 500) — how it meets Art. 27¹ is unclear (U) |
| Before 2026 | all citizens' data on servers physically in Uzbekistan, every database registered (Legal 500, Aug 2025) |

- Registration is free; review takes 5 working days under Cabinet Resolution No. 71 of 8 Feb 2020 (V, pactum.uz); Legal 500 (2025) said up to 15 days.
- Penalties: Administrative Code art. 46², Criminal Code art. 141²; amounts not read (U); access restriction has been applied in practice (V, pactum.uz).

### Cross-border: Cabinet Resolution No. 415, 29 Jul 2026, in force 3 Aug 2026

- Annex lists 49 entries: all 27 EU states, Iceland, Liechtenstein, Norway, Andorra, Switzerland, UK (with Guernsey, Jersey, Isle of Man), Faroe Islands, Russia, Israel, Canada, the US, Argentina, Brazil, Uruguay, Hong Kong, Korea, Singapore, Japan, New Zealand.
- United States counts only for companies in the EU-US Data Privacy Framework.
- Absent: Kazakhstan, UAE, Turkey, mainland China, India, Australia.
- Transfer to a listed state through systems built on international agreements: automatic, no notice, leak-prevention measures required.
- Transfer to an unlisted state: allowed if the operator and database owner meet conditions the authority sets; those conditions are not yet published.
- Item 7 gives the Ministry of Internal Affairs three months to approve SCC / BCR requirements: due about 29 Oct 2026 (computed from the resolution date).
- A leak during cross-border transfer: notify the authority within 24 h, causes and remedies within 72 h.
- The Department of Migration and Personalization under the Ministry of Internal Affairs proposes list changes; yuz.uz names the State Center for Personalization as authorized body — the sources name the regulator differently (U).

### The open question: «пользователи услуг операторов телекоммуникаций»

- Reading A, likely: data an operator holds about its subscribers: SIM numbers, IMSI / IMEI, passport data, session and base-station metadata (Settle Advisory examples, summarized).
- Reading B: any datum of a person who uses an Uzbek operator, which is almost every adult; it would swallow the amendment, so it is implausible but untested.
- A phone number a product collects itself is outside Reading A and inside Reading B.
- Searched in Russian and English: no regulator guidance, no law-firm analysis addressing an app that stores phone numbers.
- The Diplomat (Oct 2026, search snippet only) reports experts calling the drafting unclear and no body to ask for authoritative guidance.

### Hosts

- Provider, region and host names stay out of this public file; the platform's private deployment docs hold them.
- The control host runs the deploy consoles and no product; Ocharo has its own products host.
- WoW2 has no products host yet; Form 3 needs one (Q4).
- Wheelhouse adds a hosting provider as one small integration, so an onshore host stays one entry away.

### What it means per form

| Form | Data | Verdict |
|---|---|---|
| 2 · Ocharo | staff and members, invite-only | offshore is lawful if the host sits in a listed state; registration not needed under Reading A |
| 3 · WoW2 | portfolio users, mostly outside Uzbekistan; Haven and Arcade add Uzbek users | same; the exposure grows with Uzbek-audience products |

- Verify the Ocharo host's datacenter country against the 49-state list before Ocharo's instance holds a real phone (U).
- Telegram bot, Log in with Telegram and Gateway move the phone to Telegram, which is not on the list (U: Telegram's operating entity not checked); keep consent text and a stored consent record.
- Foreign users of an Uzbek-audience product fall outside the citizens rule, but a `+998` prefix is the only proxy we hold.
- Decision: treat Reading A as the working assumption, record it in the spec, and obtain a written Uzbek-counsel opinion or a regulator reply before Form 3 onboards an Uzbek-audience product at scale.
- Design for a switch: an instance on an onshore host needs only a Wheelhouse provider entry and a bound domain.
- Residency becomes a dimension of D2: one instance per owner and per residency, so an Uzbek-audience group can leave Form 3 for its own onshore instance.
- Keep phone numbers in one table with one column set, so moving or hashing them is a migration of one place.

---

## 2. Hosting topology

### Per form

| Aspect | Form 2 · Ocharo | Form 3 · WoW2 |
|---|---|---|
| Host | Ocharo's products host | a WoW2 products host; not the control host (finding 1) |
| Issuer | `https://id.<ocharo domain>`; dev `https://id-dev.<ocharo domain>` | `https://id.<wow2 domain>`; dev likewise |
| Bot | Ocharo's own bot per environment | WoW2's own bot per environment |
| Database | own database and role on the host's PostgreSQL, no superuser | same |
| Containers | one `identity` service; PostgreSQL from the platform | same |
| Sites | `id` public, `console` private | same |
| Brand | Ocharo theme (`theme-ocharo`) | per-app themes |

- One bot per environment, because a bot has exactly one webhook URL: a shared bot lets dev steal prod's updates.
- Dev and prod never share keys, bot, database or issuer.
- Descriptor v2 lets one service serve several sites with path allow-lists; a site answers 404 outside its paths.
- Public site `id` lists only: `/connect`, `/.well-known`, `/account`, `/login`, `/telegram/webhook`, static assets.
- Private site `console` lists the admin UI and `/api/admin`; Wheelhouse binds it to the private network only, never public.
- Keep the app-level check too: admin routes reject a request whose `Host` is not the console host.
- The service answers `/health` (descriptor rule); readiness covers database reachable, a non-expired signing key loaded, migration level current.
- Provider health (Telegram, Gateway, Eskiz) reports as metrics, never as readiness: a provider outage must not restart the container.
- `verify` check after each dev rollout: discovery document, JWKS, test-persona sign-in, code exchange, through the descriptor's `verify` settings.
- Open (U): the descriptor keys `product:` to one slug per repository; two owners' instances of one image have no stated model in Wheelhouse (two products from one repo, or an image-only service). Decide before sequencing step 4.

### Single or several instances

- Start with one container per instance; Wheelhouse replaces a container with a restart window and promises no zero-downtime; the descriptor has no replica count.
- Build every instance N-ready anyway; the table below is the checklist.
- Impact of a 10–30 s window: apps hold cookie sessions and verify tokens offline, so only new sign-ins and refreshes fail.
- Mitigate: client module retries refresh with backoff; access tokens last 10–15 min; deploys avoid peak sign-in hours.
- Rolling or blue-green replacement is a Wheelhouse backlog idea; revisit when a consumer measures the window.

### What every instance must share

| State | Home | Rule |
|---|---|---|
| Data Protection key ring | PostgreSQL table, keys wrapped by a certificate from a host file | never a local volume; backups and replication carry it; CVE-2026-40372 makes ring revocation a runbook step |
| OIDC signing and encryption keys | host files rendered by Wheelhouse | identical on every instance; see section 3 |
| OpenIddict applications, authorizations, tokens | PostgreSQL | author the four tables as hand-written SQL migrations; an OpenIddict 8 preview already needed a schema update |
| OTP codes and attempt counters | PostgreSQL, hashed | durable (SDK slice I15); a regenerated code never resets the attempt count |
| SSO sessions and refresh tokens | PostgreSQL | server-side rows so revoke and "sign out everywhere" work |
| Account lockout and per-phone limits | PostgreSQL | security-relevant counters survive restarts |
| Per-IP and per-client flood limits | in-memory per instance; Valkey (`needs: [valkey]`) once N > 1 | an approximation is acceptable for floods |
| Singleton jobs: outbox, key check, pruning | PostgreSQL advisory lock | run on the primary server only (redundancy model) |
| Bot update dedupe | PostgreSQL, keyed by `update_id` | webhook retries must not double-send codes |

- OpenIddict has no automatic token cleanup in the store; its Quartz.NET integration prunes, the SDK uses Hangfire, so write a prune job (U).
- Argon2id and concurrent hashing: cap concurrency so a sign-in flood cannot starve the container.

### Backups and restore

- Today a prod deploy snapshots databases and named volumes on the same disk — a restore point, not a backup.
- Off-host encrypted backups wait on Wheelhouse's planned backup work; the lifecycle rule already requires them before public cutover, so they gate Ocharo's instance.
- Identity backup set: the database (users, sessions, audit, encrypted key rows if used) and the Data Protection ring; the host KEK and signing files come from the vault, never from the backup.
- Restore proof: restore into an empty database, start with the vault values, check JWKS `kid`, sign in a test persona, verify the audit chain.
- A restore from an older backup resurrects revoked sessions, refresh tokens and removed members.
- Post-restore step, always: rotate every security stamp, purge sessions and refresh tokens, re-run member removals logged after the backup time, rotate the Data Protection ring.
- Proposed recovery point 1 min with a secondary and 24 h without; for identity the 24 h case also means up to 24 h of lost sign-in log and OTP state.

### Zero-downtime migrations

- Expand then contract: a migration must keep the previous release image working, because an image rollback never undoes a migration.
- Declare `rollbackCompatible` only for such releases; a destructive step ships one release after the code stops using the column.
- The bespoke migrator runs on boot; with one container the old one has stopped, so there is no mixed-version window, but rollback is the reason to keep it additive.
- OpenIddict upgrades that change its schema ship as their own release with a migration test in `Tests.Migrations`.

### Custom domain per instance

- The issuer appears in every token and every client's configuration; changing it signs everyone out and breaks every relying party.
- Bind the real domain before any app integrates; an sslip.io baseline host names one server's IP and cannot move.
- Wheelhouse's domain binding is still planned, so Ocharo's instance depends on it (finding 3).
- Set the issuer explicitly in OpenIddict; never derive it from the request `Host`.
- Public site sits behind a CDN proxy with a DNS-01 certificate; the proxy's address range joins `Deployment:TrustedProxies`.
- Cookies: `__Host-` prefix, host-only, one cookie scope per instance; apps on other domains work because OIDC is redirect-based.
- Third-party-cookie blocking breaks session-management iframes and front-channel logout, which supports back-channel only in P1.

### OpenTelemetry

- Meters: `identity.signin.attempts{method,result,reason,client}`, `identity.code.sent{channel,result}`, `identity.code.delivery_seconds`, `identity.refresh.reuse_detected`, `identity.lockouts`, `identity.key.days_to_expiry{kind}`, `identity.outbox.lag_seconds`, `identity.audit.chain_ok`.
- Traces for the authorize, token, bot-webhook and provider calls; a trace id on every audit row.
- Labels never hold a phone, email, IP or user id; client id and country code are fine.
- Exporter: OTLP configured by a setting, off by default, because Logs (#6) and Monitoring (#7) are not built; a private `/metrics` path serves a scraper later.

---

## 3. Keys and secrets

### Inventory

| Secret | Kind | Used for | Compromise impact |
|---|---|---|---|
| OIDC signing key | RSA (RS256 must be advertised per OIDC Discovery, U) plus optional ES256 | ID and access token signatures | forge any token for any user |
| OIDC encryption key | RSA | OpenIddict encrypts codes, refresh and device tokens always; it can skip access tokens | read refresh tokens, mint nothing alone |
| Data Protection ring | per-key AES / HMAC | SSO cookie, antiforgery, passkey ceremony state | forge sessions; CVE-2026-40372 did exactly this |
| Ring wrapping certificate | X.509 | encrypts ring keys at rest | read the ring from a database copy |
| Purpose-token HMAC key | 32 random bytes | email-link and reset tokens bound to security stamp | forge reset links |
| Database role password | string | PostgreSQL access | read and write everything but keys |
| Telegram bot token | string | send codes, receive updates, set webhook | impersonate the bot, hijack updates |
| Telegram webhook `secret_token` | 1–256 chars | authenticate inbound updates | spoof bot updates |
| Log in with Telegram client secret | string | token exchange at `oauth.telegram.org` | impersonate our relying party |
| Gateway token | string | send codes; also the HMAC key (SHA-256 of the token) for delivery reports | spend money, forge delivery reports |
| Eskiz / SMS credentials | strings | SMS (P2) | spend money |
| OAuth client secrets, up to 17 | strings | social sign-in (P2) | impersonate our app at the provider |
| Outbound webhook signing secrets | strings | webhooks (P2) | forge events |
| Ops-bot token | string | alert delivery | read alerts, send false ones |

- Agents never type or print a value (`wow-two-ws/conventions/security/secrets/secrets.md` § Agents); the owner enters each in Wheelhouse.

### Storage options

| Option | For | Against |
|---|---|---|
| Host files rendered by Wheelhouse's vault (D10 recommendation) | matches the secrets rule (read-only files in a private folder); typed `secret: true` keys live in the vault with versions and expiry flags; no new infrastructure; audited deploys | rotation is a vault edit plus a deploy (restart window); no hot reload; a PEM travels as a JSON string value |
| Keys as encrypted database rows, rotated by an SDK job | rotation without redeploy; one source for N instances; backups consistent with data | OpenIddict reads credentials from options at startup, so live reload is custom work near a security boundary (U); still needs one host KEK |
| KMS or HSM | ASVS L3 13.3.1 / 13.3.3 | no provider in the stack, new network dependency and cost |

- Pick host files for P1 and keep the key source behind a seam (`ISigningKeySource`), so database-held keys can follow.
- Slice I17 becomes "key-source seam, startup validation, rotation procedure", not a key store.
- Use X.509 certificates, not raw keys: OpenIddict ignores a certificate that is not yet valid and prefers the one expiring furthest out, so a staged `NotBefore` activates a new key without a deploy.
- Startup fails closed: no persisted signing key, an ephemeral key, no Data Protection ring or a persona sign-in enabled under `Production` all refuse to boot.
- Generate keys with a CLI inside the image (`docker exec … keys generate` printing PEM to stdout), run by the owner; never in CI or by an agent.
- Two P1 spikes (U): does the JWKS publish a certificate whose `NotBefore` lies in the future; do two certificates plus a staged date behave as the docs describe.

### Rotation with JWKS overlap

| Step | When | Action |
|---|---|---|
| 1 publish | T0 | add K2 (`NotBefore` = T0 + 24 h), deploy; JWKS should carry K1 and K2 (spike 1, U) |
| 2 activate | T0 + 24 h | K2 becomes the signing key by its date; no deploy |
| 3 retire | activation + 24 h | remove K1 from settings, deploy |
| encryption key | retention | keep the old key until the last refresh token it sealed expires (absolute lifetime + 1 day) |

- Fallback if the JWKS omits future-dated certificates: activate K2 at once; our own client module refetches on an unknown `kid`, so only foreign relying parties lose.
- 24 h covers foreign caches; our client module caches JWKS for at most 1 h and refetches on an unknown `kid`, at most once a minute.
- Cadence (proposal): signing every 90 days, encryption every 180 days; the vault's expiry flag raises each (it already flags old and expiring secrets).
- ASVS 13.1.4 (L3) asks for a documented schedule; adopt it at L2.
- Access tokens stay unencrypted JWTs (`DisableAccessTokenEncryption`) so resource servers verify offline from JWKS; codes and refresh tokens stay encrypted.
- Lifetime proposals: code 60 s, access 10–15 min, ID 5 min, refresh sliding 14 days with an absolute 90 days (members); operators re-authenticate within 12 h.

### Compromise runbook

| Event | Steps |
|---|---|
| Signing key leaked | generate K3; deploy with K3 only (outstanding tokens die); revoke all OpenIddict tokens and authorizations; rotate stamps; rotate the ring; send back-channel logout to every client; review the sign-in log from the suspected time; find the leak path (vault audit, file modes) |
| Encryption key leaked | swap the key; every code and refresh token sealed under it dies, so all users sign in again; then as above |
| Data Protection ring leaked or patched late | upgrade to `Microsoft.AspNetCore.DataProtection` 10.0.7 or later, then `RevokeAllKeys`; users sign in again; Duende advises rotating signing keys too |
| Bot token leaked | BotFather `/revoke`; new token into the vault; deploy; `setWebhook` with a new `secret_token`; flush live codes; warn users |
| Gateway token leaked | revoke in the Gateway account; set the spend cap to zero until rotated; reconcile charges |
| OAuth client secret leaked | rotate at the provider, update the vault, deploy |
| Host compromised | assume every secret on the host leaked: full rotation of the rows above, re-enroll the host from the kit, restore data, run the post-restore step |
| Admin session stolen | revoke all operator sessions, rotate the ring, review admin audit rows since the theft |

- CVE-2026-40372: forged cookies and decrypted payloads in `Microsoft.AspNetCore.DataProtection` 10.0.0–10.0.6, fixed in 10.0.7 (21 Apr 2026); patching alone does not invalidate tokens obtained earlier.
- Confirm which Data Protection version the be SDK pins (U).

---

## 4. Threat model

Rows follow STRIDE per flow. "Gap" is what the spec or SDK does not yet say.

| # | Threat | Mitigation present | Gap → action |
|---|---|---|---|
| 1 | OTP brute force (S, E) | 6 digits, 5 min, 5 attempts, hashed, one live code per phone; ASVS 6.5.4 accepts 6 random digits | attempt counter per phone must survive regeneration (NIST 3.1.3.2); bind the code to the sign-in request (ASVS 6.6.2); per-IP counter before the per-phone one |
| 2 | Code pumping and message cost (D) | P1 bot is free and can message only users who started it; 30 s resend | paid channels (Gateway about $0.01 per code, Eskiz) ship only with a default-deny country list, a daily spend cap and CAPTCHA; `checkSendAbility` is a free pre-check |
| 3 | SIM swap and number recycling (S) | link is keyed by phone at first claim only | after linking, identity keys on the Telegram user id; never match an account by the `phone_number` claim of Log in with Telegram; phone change needs a second factor or an admin step; new-device alert (P2) |
| 4 | Telegram link-by-contact (S) | `contact.user_id` equals sender; one-time `start` token; contact phone equals invited phone | token ≥ 128 bits, single use, 15 min expiry; `start` payload limit is 64 characters from `A-Z a-z 0-9 _ -` (U); notify the admin on claim; webhook `secret_token` checked in constant time |
| 5 | Session theft (S, E) | server-side sessions, rotating refresh, session list and revoke (P1) | `__Host-` cookies, `HttpOnly`, `SameSite=Lax`; re-authenticate before changing phone, email or MFA (ASVS 7.5.1); strict CSP and no third-party assets on hosted pages (RFC 9700 4.2.4) |
| 6 | Refresh-token replay (S, E) | rotation, reuse revokes the family (SDK I8); OpenIddict rolling tokens (U) | one mechanism only: use OpenIddict's store in the AS, I8 stays Form 1; set an absolute lifetime (ASVS 10.4.8); confirm reuse revokes the authorization and the leeway (U) |
| 7 | Open redirect, redirect-URI abuse (T, I) | clients by config; OpenIddict compares against registered URIs (exact match unread, U) | register exact HTTPS URIs, no wildcards; hosted-page `returnUrl` local-only; an invalid `redirect_uri` renders an error page, never a redirect (RFC 9700 4.11.2); `post_logout_redirect_uri` exact |
| 8 | CSRF (T) | PKCE / `state` in the client module | antiforgery on hosted forms and the console API, `Origin` check, `SameSite`; logout needs `id_token_hint` or a confirmation (ASVS 10.6.2) |
| 9 | Clickjacking on hosted pages (T) | none in the spec | RFC 9700 4.16 says MUST: `Content-Security-Policy: frame-ancestors 'none'` and `X-Frame-Options: DENY`; test it |
| 10 | Admin console takeover (E) | owners and admins use passkeys by default (P2); the console is a separate surface | phone + Telegram code alone guards the top account: passkey or TOTP required for operators in P1 (ASVS 6.3.3); console on a private site; step-up before destructive actions; first admin created by CLI on the host, never by a setup page; break-glass recovery is a host CLI, not email |
| 11 | Impersonation abuse (E, R) | P3 with banner and audit row | time-box 30–60 min; `act` claim and `impersonated` flag so apps can block destructive actions; no impersonating admins; step-up to start; audit start, end and every request |
| 12 | Account enumeration (I) | none stated | the phone step answers identically for known and unknown numbers; the bot deep link arrives with the invite, not from a recognized-phone page; equal timing; throttle per IP |
| 13 | Denial of service (D) | lockout 5 / 5 min per account | lockout becomes a targeted-lockout weapon against one admin: progressive delay, per-IP + per-account, passkey sign-in unaffected; Argon2id concurrency cap; reverse-proxy rate limit; a CDN in front; webhook rejects a bad secret header before any work |
| 14 | Token confusion (S) | per-app audience (P1) | resource servers accept only their own `aud` (ASVS 10.3.1); `typ: at+jwt`; algorithm allow-list; identify users by `iss` + `sub` |
| 15 | Back-channel logout SSRF (I, D) | clients by config | outbound allow-list limited to registered `backchannel_logout_uri`; no redirects, short timeout, no private ranges unless registered (ASVS 13.2.4); outbox with retry cap |
| 16 | Forged Telegram OIDC token (S) | verify via JWKS | pin `iss` `https://oauth.telegram.org`, `aud` = bot id, algorithm allow-list (RS256), `nonce` and PKCE S256 on our request |
| 17 | Backup resurrection (T) | none | post-restore epoch bump (section 2) |
| 18 | PII in logs and telemetry (I) | masked phone in the sign-in log | no phone, code, token or secret in logs, labels or traces; a lint test over log templates |
| 19 | Test mode in production (E) | none (test mode is new) | persona sign-in and fake channels compile into an `Testing` host mode only; production refuses to start with them; an integration test asserts the refusal |
| 20 | Supply chain (T) | Renovate in repos | OpenIddict advisory GHSA-925x-4h4v-2792 (audience validation in client assertions) fixed in 7.7.0 / 8.0 preview 4; pin 7.7.2 or later and subscribe to advisories |

---

## 5. Standards baseline

### OWASP ASVS 5.0.0 (30 May 2025)

- Target Level 2 for both forms: L2 "most applications should strive" for, and it is cumulative (L1 + L2 ≈ 70% of requirements).
- Chapters in scope: V6 Authentication, V7 Session Management, V9 Self-contained Tokens, V10 OAuth and OIDC, V11 Cryptography, V13 Configuration, V14 Data Protection, V16 Logging; V3 Frontend and V4 API for the hosted pages and console. V9 was not read (U).
- Adopt these L3 items for the operator surface: 6.3.3 phishing-resistant factor (passkeys), 6.3.5 and 6.3.7 notifications, 6.3.8 anti-enumeration, 6.4.6 admins cannot choose a user's password, 13.1.4 documented rotation schedule.
- Declare these L3 items out of scope with a rationale (ASVS 6.3.3 asks for one):

| ID | L3 requirement | Why out |
|---|---|---|
| 6.6.1 | no SMS / PSTN OTP | Eskiz SMS is a P2 adapter; Telegram bot codes are not PSTN |
| 6.3.6 | email never a factor | P2 email codes and magic links |
| 10.3.5, 10.4.14 | sender-constrained tokens (DPoP, mTLS) | T2 skipped DPoP; BFF cookie clients lower the need |
| 10.4.13 | PAR always | first-party clients only |
| 13.3.1, 13.3.3 | HSM / isolated crypto module | no KMS in the stack |

Items that bite at L1 and L2:

| ID | Level | Requirement | Where we stand |
|---|---|---|---|
| 10.4.1 | 1 | exact match against registered redirect URIs | configure and test |
| 10.4.2 | 1 | code single use; reuse revokes issued tokens | OpenIddict behavior unverified (U), test |
| 10.4.3 | 1 | code ≤ 10 min (≤ 1 min at L3) | set 60 s |
| 10.4.4 | 1 | no implicit, no password grant | never enable either |
| 10.4.5, 10.4.8 | 1, 2 | refresh rotation, reuse revokes; absolute expiry | set the absolute lifetime |
| 10.4.6 | 2 | PKCE required, `plain` rejected | `RequireProofKeyForCodeExchange()` (global or per client); confirm `plain` is not advertised (U) |
| 10.4.9 | 2 | users revoke refresh tokens in the AS UI | session list in P1 |
| 10.4.10 | 2 | confidential clients authenticate on token and revocation | client secrets stored hashed |
| 10.6.1, 10.6.2 | 2 | response type `code` only; forced-logout DoS defense | disable implicit / hybrid; `id_token_hint` or confirm |
| 10.7.1–10.7.3 | 2 | consent when client identity is not assured | first-party clients skip it; third-party consent is P3 and must prompt |
| 6.2.12 | 2 | breached-password check | SDK check fails open; document the deviation |
| 6.3.3 | 2 | MFA, or a combination of single factors | operators in P1 |
| 6.5.1–6.5.5 | 2 | OOB codes single use, CSPRNG, ≥ 20 bits, ≤ 10 min | met by the spec's code once single use and a CSPRNG are stated |
| 6.6.1 | 2 | SMS only for validated numbers and beside a stronger option | applies when Eskiz ships |
| 7.1.1–7.1.3 | 2 | document timeouts, concurrent sessions, SSO session coordination | write them in the spec |
| 7.4.2, 7.4.5 | 1, 2 | disabling a user ends sessions; admins end sessions | in the Ocharo slice |
| 7.4.3 | 2 | offer to end other sessions after a factor change | add to account page |

### NIST SP 800-63B-4

- Passwords: ≥ 15 characters when the only authenticator, ≥ 8 inside MFA, accept ≥ 64, no composition rules, no periodic change, blocklist with a stated reason (3.1.1.2). The spec's 8–128 length needs the 15 rule where a password is the sole factor.
- Throttle: at most 100 consecutive failures per authenticator before disabling it (3.2.2); our 5 is stricter.
- Out-of-band codes: ≥ 6 digits, ≤ 10 min, single use, rate limited; a new code must not reset the failure count (3.1.3.2).
- Email must not serve as an out-of-band authenticator (3.1.3.1); P2 email codes and magic links deviate; record that as an accepted deviation.
- PSTN is restricted (3.1.3.3): risk assessment, an unrestricted alternative, SIM-change and porting indicators considered.
- Phishing resistance: offer at least one option at AAL2 (2.2.2); syncable passkeys are allowed at AAL2, not AAL3.
- Recovery (4.2): saved codes ≥ 64 bits, hashed; issued codes by text last 10 min; AAL2 recovery needs two methods; every recovery sends a notice.
- Notifications (4.1.2.1, 4.6): tell the subscriber through an independent channel when an authenticator binds; at least two addresses per account.
- Sessions (5.1, 2.1.3–2.3.3): ≥ 64-bit secrets, erase on logout, `Secure`, `HttpOnly`, `SameSite`, `__Host-` prefix; AAL2 reauthenticate within 24 h overall and 1 h inactive (SHOULD).
- Design target: members AAL1 (one out-of-band factor), operators and owners AAL2 (passkey, or TOTP plus a second factor). A design target, not a compliance claim.

### OAuth 2.0 Security BCP (RFC 9700)

| Item | § | OpenIddict | We configure or test |
|---|---|---|---|
| exact redirect URI match, no open redirectors | 2.1 | validates against registered URIs (exact match unread, U) | exact HTTPS URIs; local-only `returnUrl` |
| PKCE supported, enforced, downgrade rejected | 2.1.1, 4.8.2 | opt-in enforcement; downgrade behavior unread (U) | enforce per production client; publish `code_challenge_methods_supported` |
| no implicit, no password grant | 2.1.2, 2.4 | flows are opt-in | never enable |
| code single use, revoke on reuse | 4.2.4 | unverified (U) | test |
| refresh rotation or sender constraint, reuse detection | 2.2.2, 4.14.2 | rolling tokens (U) | one store; absolute lifetime |
| sender-constrained access tokens (SHOULD) | 2.2.1 | not in the stable line (U) | skip; short tokens and BFF clients |
| audience-restricted access tokens | 2.3 | per-client resources | set audience per app |
| issuer in the authorization response (RFC 9207) | 4.4.2 | unverified (U) | matters only to clients of several issuers; ours have one |
| no 307 on credential-carrying redirects | 4.12 | — | use 303 / 302; no `RedirectPreserveMethod` |
| proxy sanitizes inbound headers, link protected | 4.13 | — | the reverse proxy overwrites `X-Forwarded-*`; Wheelhouse sets `TrustedProxies` |
| prevent clickjacking | 4.16 | none | CSP `frame-ancestors`, `X-Frame-Options` |
| no CORS on the authorization endpoint | 2.6 | none by default | keep it off |
| no third-party resources on authorization pages | 4.2.4 | — | self-hosted assets, `Referrer-Policy: no-referrer` |
| publish AS metadata | 2.6 | discovery and JWKS | Config OP plan |
| inform, never redirect, on a bad client or redirect URI | 4.11.2 | — | error page |

---

## 6. Conformance

### Plans that apply

| Plan (OIDF suite) | Applies | Note |
|---|---|---|
| Config OP | yes | discovery and JWKS; exact plan id unverified (U) |
| Basic OP: `oidcc-basic-certification-test-plan[server_metadata=discovery][client_registration=static_client]` | yes | needs static clients: two with `client_secret_basic`, one with `client_secret_post` |
| RP-initiated logout: `oidcc-rp-initiated-logout-certification-test-plan[response_type=code][client_registration=static_client]` | yes | |
| Back-channel logout: `oidcc-backchannel-rp-initiated-logout-certification-test-plan[response_type=code][client_registration=static_client]` | yes | fails until our custom logout-token delivery exists |
| Implicit OP, Hybrid OP | no | disabled by design (ASVS 10.6.1) |
| Dynamic OP | no | no dynamic registration |
| Form Post OP | no | skip unless `form_post` is turned on |
| Third-party-initiated login | no | P3 |
| Session management, front-channel logout | no | third-party cookies; P2 |

- Plan names above, except Config OP, appear in a SimpleSAMLphp CI recipe, which checks out a pinned suite release tag (v5.1.39 in its text).
- OpenIddict is not in the certification program; its README says certifying the framework would not make a deployment compliant, so our deployment's results matter.
- OpenIddict's samples include `Contruum.Server`, built for the certification tool.

### Self-hosted run in GitHub Actions

1. Job `conformance` in the identity repo, after the image build; runs on tags and nightly, not on every push (minutes cost; duration unmeasured, U).
2. Start PostgreSQL and the identity image in `Testing` mode on a Docker network: conformance clients, persona sign-in, fake channels.
3. Check out `gitlab.com/openid/conformance-suite` at a pinned tag; build with `MAVEN_CACHE=./m2 docker-compose -f builder-compose.yml run builder`.
4. `docker-compose up` starts the Java app and MongoDB; the suite answers on `https://localhost:8443` and `localhost.emobix.co.uk`.
5. Run `scripts/run-test-plan.py --expected-failures-file … --expected-skips-file … "<plan>[…]" config.json` per plan; the config names `alias` and `discoveryUrl`.
6. Keep the expected-failures and expected-skips files in the repo; review them on each suite bump; upload logs as artifacts.
7. The same suite also runs against Form 1: start the module's test host on a real port so the Java container reaches it (capability-forms rule: one suite, module and image).

Gotchas:

- The Basic OP plan sends code flow requests without PKCE (U): register the conformance clients without the PKCE requirement, and keep that registration out of production configuration.
- The suite's scripted browser is a headless engine (HtmlUnit, U): a Vue sign-in page may not run; serve a plain server-rendered persona form in `Testing` mode only.
- Tests such as `max_age=1` and `prompt=login` re-authenticate within seconds: persona sign-in has no resend delay or rate limit in `Testing` mode.
- The issuer must equal the discovery URL as the suite sees it; use a stable Docker hostname and TLS the suite accepts (U).
- Back-channel tests need the OP to reach the suite's simulated RP over the Docker network; the outbound allow-list must permit it in `Testing` mode.

### Formal certification

- Fee per new deployment: OpenID Connect $700 for OIDF members, $3,500 otherwise; one payment covers several profiles within a calendar year; OP and RP are separate.
- Open-source projects may qualify for a waiver under a separate policy (criteria unread, U); OIDF membership cost not checked (U).
- Results must come from the OIDF-hosted suite at `certification.openid.net` (Google or GitLab login) with the OP publicly reachable; no FAILED or INTERRUPTED result in a profile (U whether local runs count: the OP page describes only the hosted suite).
- Self-certification: the implementer submits logs, a certification-of-conformance document and accepts the terms.
- Recommendation: run the local suite in CI from P1; run the hosted suite once for a baseline; defer paid certification until an external consumer asks. First-party clients gain little from the badge.

---

## 7. Operations

### Alerts

Thresholds are starting values (U) to tune in the first month.

| Alert | Signal | Threshold | Action |
|---|---|---|---|
| Sign-in failure spike | failed / attempted per method and client, 5 min | > 30% and > 20 events | check stuffing, throttle at the reverse proxy or CDN, enable CAPTCHA |
| Code delivery failure | bot send errors, Gateway status and refunds, delivery p95 | success < 95% in 15 min, p95 > 10 s | switch channel, check Telegram status |
| Key expiry | `identity.key.days_to_expiry` per key | warn 30 d, page 7 d | start rotation; the vault flags it too |
| Refresh reuse | `identity.refresh.reuse_detected` | > 3 per hour | review the sign-in log, suspect token theft |
| Lockout surge | lockouts per hour, any operator lockout | any operator; > 10 per hour | check targeted lockout |
| Paid-channel spend | cost vs daily cap | 80% of cap | review country mix, pause channel |
| Bot webhook | `getWebhookInfo` error and pending count, polled every 5 min | any `last_error_message`, pending > 100 | fix endpoint, re-set webhook |
| Outbox lag | undelivered back-channel logout | > 5 min | check client endpoints, dead letters |
| Backups | newest off-host backup age; last restore drill | > 26 h; > 100 days | run backup; run drill (quarterly rule) |
| Health, TLS, disk | `/health`, certificate expiry, disk | 3 failures; < 14 d; > 75% | Wheelhouse vitals and expiry already flag the last two |
| Admin events | new operator, role to owner, impersonation start, key operation, console sign-in from a new country | every event | immediate message to the ops chat |
| Audit chain | `identity.audit.chain_ok` | false | treat as tampering until proven otherwise |

- Destination until Monitoring (#7) exists: an ops notifier inside Identity sending Bot API messages to a private ops chat through its own bot and token.
- Key and secret expiry also raise through Wheelhouse Issues when each secret carries an expiry in the vault.
- Metrics leave through OTLP or `/metrics` once a collector exists; dashboards are bought (Grafana or SigNoz), not built.

### Incident runbook items

- Key leak, Data Protection ring leak, bot or Gateway token leak, host compromise: section 3 runbook.
- Credential stuffing: tighten the per-IP limit at the reverse proxy or CDN, add CAPTCHA, shorten code attempts, never switch off lockout.
- Pumping: set the paid channel's spend cap to zero by configuration, no deploy; review sends by country.
- Telegram outage or bot ban: P1 has one channel, so keep operators' saved recovery codes and a host CLI to issue a one-time sign-in; Gateway is the P2 fallback.
- Compromised member: disable, rotate stamp, unlink Telegram, revoke sessions, push back-channel logout, read the member's sign-in history.
- Bad release: roll back the image only if the release declared `rollbackCompatible`; otherwise fix forward.
- Restore: run the post-restore step before reopening sign-in.
- Leak involving Uzbek citizens' data during a cross-border transfer: notify the authority within 24 h and detail within 72 h (Resolution 415 item 4).
- Publish `/.well-known/security.txt` on the public site.

### Audit trail

- P1 destination: an append-only table `identity_audit` in the instance database; `UPDATE` and `DELETE` refused by trigger, as Wheelhouse's audit table does.
- Two streams: sign-in log (high volume, monthly partitions, retention proposed 180 days, masked phone, IP and device) and admin / security events (low volume, kept at least 2 years, proposed).
- A hash chain proves no stored row was edited or removed from the middle, not that the newest rows were kept; write a daily head-hash checkpoint to the off-host backup and the ops chat.
- Rows hold actor, action, subject, client, result, reason, trace id; never a code, token, key or full phone.
- When the Audit Log capability exists (D14, #11), Identity emits the same events into it and keeps only the sign-in log locally.
- Purge job enforces retention; the sign-in log is personal data too (IP, device, phone).

---

## Findings that change the spec

- Form 3 cannot run on the control host: the lifecycle convention keeps products off it; the spec said "WoW2 control host" → move Form 3 to a WoW2 products host, none of which exists yet.
- Data localization is not a blocker: since 27 Mar 2026 only biometric, genetic and telecom-user data must stay in Uzbekistan; the rest may live in any of 49 listed states, and only part-2 databases register → add the working assumption and the counsel step to the spec, and extend D2 to "one instance per owner and per residency".
- The telecom-user clause could cover phone numbers (Reading B) → settle with a written opinion before an Uzbek-audience product joins Form 3; keep phones in one table and keep an onshore instance option; watch Resolution 415 item 7 (due about 29 Oct 2026).
- The issuer must be a bound domain before any app integrates, and Wheelhouse's domain binding is still planned → add "domain binding" as a precondition of sequencing step 4 and never publish an sslip.io issuer.
- Phone + Telegram code alone guards the console → P1 requires a passkey or TOTP for operators, the console on a private site with a path allow-list on the public site, and a CLI-created first admin.
- Identity must key on the Telegram user id after first link, never on the phone claim → add to the Ocharo slice (SIM swap, number recycling, `phone` scope of Log in with Telegram).
- Keys: host files rendered by Wheelhouse as X.509 certificates with staged `NotBefore`, fail-closed startup, a CLI generator → rewrite slice I17 as a key-source seam plus rotation procedure; run two spikes (JWKS publishing of future-dated certificates, OpenIddict dynamic credentials).
- Password features in P2 contradict the standards: NIST says composition rules and periodic change SHALL NOT be imposed (ASVS 6.2.5, 6.2.10) → delete composition rules, expiry and history, not just defer them; raise the sole-factor minimum to 15; record email codes and magic links as a deviation from NIST 3.1.3.1.
- OTP rules need two additions: the failure count survives code regeneration, and the code is bound to the original sign-in request (ASVS 6.6.2).
- Clickjacking defense and no third-party assets on hosted pages are absent from the spec; RFC 9700 4.16 makes the first a MUST → add both with tests.
- Conformance needs a `Testing` host mode: per-client PKCE (not global), a server-rendered persona login, no rate limit on the persona; the back-channel plan fails until the custom delivery exists; certification ($700 member / $3,500 non-member) can wait.
- Wheelhouse restarts a container with a downtime window and the descriptor has no replica count → design for N instances, accept the window, make the client module retry refresh, keep singleton jobs behind advisory locks.
- A restore resurrects revoked sessions and removed members → add the post-restore step to the runbook; off-host backups (planned in Wheelhouse) gate Ocharo's public cutover.
- CVE-2026-40372 shows a leaked Data Protection ring is a live event → store the ring in the database, pin ≥ 10.0.7, document `RevokeAllKeys`.
- Logs (#6) and Monitoring (#7) do not exist → P1 carries its own ops notifier, OpenTelemetry meters and a private `/metrics`.
- Two owners' instances of one image have no stated model in Wheelhouse (descriptor `product:` slug per repository) → decide before deploying Ocharo's instance (U).

---

## Sources

- Uzbek law: [lex.uz — ЗРУ-1125 amending law](https://lex.uz/mact/8104580) · [lex.uz — ЗРУ-547 consolidated](https://lex.uz/docs/4396428) · [lex.uz — Resolution No. 415](https://lex.uz/docs/8369688) (summaries of the pages, official Uzbek text unread)
- Commentary: [IT-TEAM SERVICE — first review of the 2026 amendments](https://itts.uz/news/pdn_local_review1/) · [IT-TEAM SERVICE — Resolution 415](https://itts.uz/news/pkm415/) · [Gazeta.uz — list of countries](https://www.gazeta.uz/ru/2026/08/04/personal-data/) · [norma.uz — countries](https://www.norma.uz/novoe_v_zakonodatelstve/kakie_strany_obespechat_zashchitu_personalnyh_dannyh) · [Settle Advisory — localization](https://settleadvisory.com/news-en/localization-of-personal-data-in-uzbekistan-transition-to-a-more-flexible-regulatory-model/) · [Legal 500 / SETTLE — compliance, Aug 2025](https://www.legal500.com/developments/thought-leadership/personal-data-compliance-in-uzbekistan/) · [pactum.uz](https://pactum.uz/solutions/personalnye-dannye-uzbekistan) (V) · [yuz.uz](https://yuz.uz/ru/news/uzbekistan-utverdil-perechen-iz-49-stran-s-ravnotsennoy-zaitoy-personalnx-dannx)
- Search snippets only (page blocked): [Dentons, 31 Mar 2026](https://www.dentons.com/en/insights/articles/2026/march/31/uzbekistan-dismantles-strict-data-localization-regime) · [The Diplomat, Oct 2026](https://thediplomat.com/2026/10/from-data-localization-to-digital-openness-uzbekistans-regulatory-shift/) · [Kun.uz, 27 Mar 2026](https://kun.uz/en/news/2026/03/27/uzbekistan-amends-personal-data-law-to-facilitate-global-payment-systems)
- Local: [identity spec](../../../product/product.md) · [T2](t2-benchmark.md) · hub `wow-two-ws/`: `conventions/development/capability-forms.md`, `conventions/deployment/servers/server-lifecycle.md`, `conventions/deployment/servers/redundancy.md`, `conventions/deployment/descriptor/deploy-descriptor.md`, `conventions/deployment/hosting/domains.md`, `conventions/security/secrets/secrets.md`, `docs/platform-decisions.md`, `docs/platform-apps.md` · Wheelhouse's deployment, architecture and backlog docs (private) · `engineering/research/identity/identity-slices.md` (from the be sdk)
- OpenIddict: [README](https://raw.githubusercontent.com/openiddict/openiddict-core/dev/README.md) · [releases](https://github.com/openiddict/openiddict-core/releases) · [credentials](https://documentation.openiddict.com/configuration/encryption-and-signing-credentials) · [token formats](https://documentation.openiddict.com/configuration/token-formats) · [token storage](https://documentation.openiddict.com/configuration/token-storage) · [permissions](https://documentation.openiddict.com/configuration/application-permissions) · [PKCE](https://documentation.openiddict.com/configuration/proof-key-for-code-exchange)
- CVE-2026-40372: [dotnet/announcements #395](https://github.com/dotnet/announcements/issues/395) · [Duende guidance](https://duendesoftware.com/blog/20260422-update-guidance-for-cve-2026-40372-aspnet-data-protection)
- Standards: [OWASP ASVS 5.0.0](https://asvs.owasp.org/) · [V6](https://raw.githubusercontent.com/OWASP/ASVS/v5.0.0/5.0/en/0x15-V6-Authentication.md) · [V7](https://raw.githubusercontent.com/OWASP/ASVS/v5.0.0/5.0/en/0x16-V7-Session-Management.md) · [V10](https://raw.githubusercontent.com/OWASP/ASVS/v5.0.0/5.0/en/0x19-V10-OAuth-and-OIDC.md) · [V13](https://raw.githubusercontent.com/OWASP/ASVS/v5.0.0/5.0/en/0x22-V13-Configuration.md) · [levels](https://raw.githubusercontent.com/OWASP/ASVS/v5.0.0/5.0/en/0x03-What-is-the-ASVS.md) · [NIST SP 800-63B-4](https://pages.nist.gov/800-63-4/sp800-63b.html) · [RFC 9700](https://www.rfc-editor.org/rfc/rfc9700.html)
- Conformance: [OIDF OP testing](https://openid.net/certification/connect_op_testing/) · [certification fees](https://openid.net/certification/fees/) · [certification overview](https://openid.net/certification/) · [instructions](https://openid.net/certification/instructions/) · [suite repository](https://gitlab.com/openid/conformance-suite) · [SimpleSAMLphp CI recipe](https://simplesamlphp.org/docs/contrib_modules/oidc/5-oidc-conformance.html)
- Telegram: [Log In With Telegram](https://core.telegram.org/bots/telegram-login) · [Bot API setWebhook, Contact](https://core.telegram.org/bots/api#setwebhook) · [Gateway API](https://core.telegram.org/gateway/api)

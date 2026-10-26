# T1 — Identity integrations

*Last updated: 2026-10-26*

> Inventory of every third-party integration `wow-two-sdk-beta.identity` will use: purpose, phase, be sdk status, licence and cost, Uzbekistan constraints, pick.

Legend: **SDK status** — `wired` = package referenced in `WoW.Two.Sdk.Beta.Be.csproj` or HTTP client in `src/`; `pinned only` = version in `Directory.Packages.props`, no reference or code; `none` = absent. **(U)** = could not verify. Phases are the spec's P1 / P2 / P3. Prices from vendor pages read 2026-10-25.

---

## 1. Protocol — OpenIddict

| Integration | Purpose | Phase | SDK status | Licence/cost | UZ constraint | Pick |
|---|---|---|---|---|---|---|
| `OpenIddict.AspNetCore` (Server + Core + Validation) | authorize, token, end-session, discovery, JWKS, introspection, revocation endpoints | P1 | pinned only: `6.0.0`, not referenced in the csproj; `Identity/Oidc` is a Microsoft OIDC *client* wrapper | Apache-2.0; free; no gated library features found; support only for the latest stable (7.x), older majors only for sponsors/ABP | none | Use `7.7.2` (2026-10-10, targets net10.0); 6.x is out of support |
| `OpenIddict.EntityFrameworkCore` | stores for applications, authorizations, scopes, tokens | P1 | pinned only (`6.0.0`) | Apache-2.0 | none | Use `7.7.2`; on net10.0 it needs `Microsoft.EntityFrameworkCore.Relational` ≥ 10.0.11, the props pin 10.0.3 → bump EF Core first |
| `OpenIddict.Client.AspNetCore` + `.WebIntegration` | consume external IdPs: Telegram, social, Entra per organization | P1 Telegram · P2 social | none (SDK uses `Microsoft.AspNetCore.Authentication.OpenIdConnect` + `AspNet.Security.OAuth.*`) | Apache-2.0; ~100 built-in providers, no Telegram | none | Use in the service for Telegram + social; keep the `AspNet.Security.OAuth.*` wrappers for Form 1 until a spike proves parity (U) |
| `OpenIddict.Validation.AspNetCore` | resource-server token validation, introspection | P1 | none (SDK validates with `JwtBearer`) | Apache-2.0 | none | Use in apps; access tokens are encrypted JWE by default, so plain `JwtBearer` needs `DisableAccessTokenEncryption()` (U, default not re-read) |
| `OpenIddict.Quartz` | prune expired tokens and ad-hoc authorizations (14 days) | P1 | none | Apache-2.0; pulls Quartz.NET 3/4 | none | Skip; call the managers' prune methods from the SDK `Jobs` module |

### What OpenIddict 7.7.2 covers

| Capability | Native | We build | Phase |
|---|---|---|---|
| Authorization code + PKCE | yes | consent UI, login UI, claims from our store | P1 |
| Refresh rotation + reuse detection | yes — rotation, redeemed-token replay revokes the authorization chain, `SetRefreshTokenReuseLeeway` | nothing; our `identity_refresh_tokens` (I8) stays for Form 1 JWT mode only | P1 |
| Client credentials | yes | client registry UI | P2 |
| Device flow (RFC 8628) | yes (`AllowDeviceAuthorizationFlow`, `DeviceAuthorization` endpoint) | user-code entry page | P2 |
| Token exchange (RFC 8693) | yes since 7.0 (`AllowTokenExchangeFlow`, audience/resource registration) | subject-token validation and policy in the pass-through handler | P2 |
| RP-initiated logout (`end_session`) | yes | logout page | P1 |
| Back-channel logout | **no** in 7.7.2 — issue #2175, planned 8.0.0-preview.5; its session entity landed in 8.0.0-preview.3 | `logout_token` sender over our session table (I16) and per-client `backchannel_logout_uri` | P1 |
| Front-channel logout | **no, and no plans** (maintainer: clunky, broken by third-party-cookie blocking) | drop it | — |
| PAR (RFC 9126) | yes (`SetPushedAuthorizationEndpointUris`, `RequirePushedAuthorizationRequests`) | nothing | P3 |
| DPoP (RFC 9449) | **no** in client, server or validation; maintainer: "at some point", prefers mTLS (native mTLS client auth merged 2026-02, 8.0 line) | nothing; skip | — |
| Introspection, revocation | yes | nothing | P1 |
| JWKS, discovery, userinfo | yes — JWKS lists every registered credential | key store, rotation job (I17) | P1 |
| Signing-key rotation | manual — register the new credential before the old expires; no key store, no automatic rotation; hot reload without restart not documented (U) | store, schedule and reload keys | P1 |
| Dynamic client registration (RFC 7591/7592) | **no** — issue #2404, milestone slipped 5 times since March 2026, now 8.0.0-preview.5 | admin API over `IOpenIddictApplicationManager` | P2 |
| Session list / revoke | no in 7.x; `Session` entity from 8.0.0-preview.3 | our sessions table (I16) | P1 |
| Access-token revocation | partial — API calls do not check authorization status unless `EnableAuthorizationEntryValidation()` (needs server DB access) or introspection | short access-token lifetime + introspection or back-channel | P1 |

### How it plugs into our store

- OpenIddict persists four entities only — application, authorization, scope, token — plus `Resource` and `Session` from 8.0 previews; it never reads users.
- Users stay in our tables: pass-through authorize/token handlers call `IUserRepository`, then build the `ClaimsPrincipal` and `SignIn(...)`; OpenIddict issues the tokens.
- Store options: built-in EF Core (SDK already runs EF Core 10 + Npgsql), EF6, MongoDB; a custom store implements `IOpenIddict{Application,Authorization,Scope,Token}Store<T>`. Pick EF Core stores.
- Schema comes from the EF model (`UseOpenIddict()` in `OnModelCreating`, key type via `ReplaceDefaultEntities<TKey>`): generate SQL with `dotnet ef migrations script` and feed it to the SDK's SQL migrator.
- 7.0 widened `OpenIddictTokens` id columns 50 → 150; 8.0 preview.3 adds `Resource` and `Session` tables and uses JSON operators in EF Core stores (PostgreSQL qualifies) → plan one migration at the 7 → 8 move.
- Majors arrive about every 12 months (7.0 on 2025-07-07, 8.0 in preview) and only the latest is supported: budget an upgrade pass per year.

### OpenIddict.Client and Telegram

- No Telegram provider in `OpenIddict.Client.WebIntegration` (docs list; issue search finds none).
- Telegram's discovery document is standard: code flow only, PKCE `plain`/`S256`, `client_secret_basic`/`post`, signing `RS256`/`ES256`/`EdDSA`/`ES256K`, **no `userinfo_endpoint`**, no refresh grant.
- Pick: register `https://oauth.telegram.org` as a generic issuer (static registration) and read `phone_number` from the ID token; spike needed, not run (U).
- Per-organization providers (Entra, Google Workspace) need runtime registrations: no built-in API; maintainer's answer (issue #2273) = `IConfigureOptions<OpenIddictClientOptions>` + `IOptionsChangeTokenSource`, or a derived `OpenIddictClientService`.

---

## 2. Credential libraries

| Integration | Purpose | Phase | SDK status | Licence/cost | UZ constraint | Pick |
|---|---|---|---|---|---|---|
| `Fido2.AspNet` (fido2-net-lib) | passkeys, usernameless | P2 (exists) | wired: `3.0.1`; `UserPasskeyService`, ceremony state sealed by Data Protection | MIT; free | none | Bump to `4.3.1` (2026-10-16): 4.2.0 fixed SSRF via AIA fetches, CBOR/ASN.1 stack-overflow DoS, MDS cache poisoning; 4.1.0 renamed `ServerDomain` → `RPID`; 4.3.0 rejects cross-origin ceremonies by default; stay off 5.0 previews (WebAuthn L3) |
| `Konscious.Security.Cryptography.Argon2` | Argon2id hashing with rehash | P1 (exists) | wired: `1.3.1` | MIT; free | none | Keep; last release 2024-06-19 (net6/net8 assets), 11.8M downloads; alternative `Geralt` 4.4.0 (libsodium, MIT, 2026-07) if managed hashing proves slow on the VPS (U) |
| HIBP Pwned Passwords range API | breached-password check (k-anonymity) | P2 (exists) | wired: own `PwnedPasswordsClient`, padded, fails open | free; no API key, no rate limit, no attribution required | none known; reachability from UZ not tested (U) | Keep as is |
| zxcvbn | strength score and meter | P2 | none (fe `passwordStrength` empty) | .NET: `zxcvbn-core` 7.0.92, last stable 2021-02, 8.0 betas 2022 (stale); JS: `@zxcvbn-ts/core` 4.2.0 (MIT, 2026-08-12) | dictionaries have no Uzbek/Russian-translit coverage out of the box (U) | fe SDK meter on `@zxcvbn-ts/core`; be keeps NIST length + HIBP; no .NET zxcvbn dependency |
| `Otp.NET` | TOTP/HOTP | P1 (exists) | wired: `1.4.0` (`TotpService`, RFC 6238 defaults) | MIT (licence not shown on the NuGet page, U); free | none | Bump to `1.4.1` (2025-12-01) |
| Recovery codes, API keys, purpose tokens | second-factor fallback, M2M keys | P1 (exists) | wired, own code, no dependency | — | none | Keep |

---

## 3. Delivery channels

| Integration | Purpose | Phase | SDK status | Licence/cost | UZ constraint | Pick |
|---|---|---|---|---|---|---|
| Telegram Bot API + `Telegram.Bot` | phone + code via own bot; link-by-contact (`KeyboardButton.request_contact`, check `contact.user_id == from.id`) | P1 | wired: `Telegram.Bot` `22.10.0.1` (latest stable 22.10.3.3); `TelegramOtpDeliveryHandler`; webhook `secret_token` validator in `Messaging`; the link flow itself is not in the SDK (Desk owns it, slice I20) | free; Bot API 10.3 (2026-08-24); ~30 msgs/s across users, ~1 msg/s per chat | `Contact.user_id` is optional and the docs do not tie it to the sender, so the sender check stays ours | Use; bump `Telegram.Bot` |
| Log in with Telegram (OIDC) | one-tap sign-in, `phone` scope | P1 | none (I20 planned) | free; needs a bot + BotFather "Allowed URLs"; client id/secret from BotFather | none found | Use via `OpenIddict.Client` generic issuer (section 1) |
| Telegram Gateway | code to any Telegram number, no bot chat | P2 | wired: `sendVerificationMessage` only (4–8 digits, TTL 30–3600 s); no `checkSendAbility`, status, revoke or callback signature check | **$0.01 per delivered code**; undelivered after TTL refunded automatically; own number free; payment **in TON** (gateway.telegram.org) | crypto-only funding from Uzbekistan (practicality and legality U); the number must belong to a user who shared it with the service | Defer; only for members who cannot use the bot |
| Eskiz (UZ SMS) | SMS OTP in Uzbekistan | P2 | wired: `AddEskizSmsBroker`, HTTP, token cached, default sender `4546`, moderated templates only | ~85 UZS per SMS to all UZ operators (≈$0.007 at ~12.5k UZS/USD, U) per a Bitrix24 listing, not Eskiz's own page; flat, no subscription | contract with Eskiz IT Company for login + secret key; services only for legal entities and residents of Uzbekistan; OTP wording must match an approved template | Pick for UZ SMS; confirm price and entity requirement with Eskiz |
| Other UZ SMS: PlayMobile, TextUp, Sayqal | fallback to Eskiz | P2 | none | price not found (U) | PlayMobile pushes delivery reports; TextUp has a template registry + OTP tariff flag and needs exact template match | Defer; add one only if Eskiz fails |
| Twilio SMS | SMS outside UZ | P2 | wired: HTTP broker (`Twilio` `7.7.4` pinned only) | **$0.4571 per SMS to Uzbekistan** (+ $0.001 failed fee); alphanumeric sender id free | sender-id registration rules for UZ not stated (U); paying from an Uzbek card untested (U) | Non-UZ fallback only; ~60× Eskiz for UZ |
| Vonage SMS | SMS outside UZ | P2 | wired: HTTP broker (`Vonage` `7.16.0` pinned only) | UZ rate not found (U); a reseller sheet shows €0.337 | as Twilio | Non-UZ fallback only |
| WhatsApp (Meta Cloud API direct, or Twilio) | OTP by WhatsApp authentication template | P2 | wired: `MetaWhatsAppBroker`, Twilio broker | per delivered message since 2025-07-01; UZ falls in "Rest of Asia Pacific" (not named on the rate card); authentication ≈ $0.013 per a reseller table, undated (U) | Meta business verification + approved authentication template (U) | Defer; behind SMS |
| Amazon SES | transactional email (D13) | P2 | wired: `AWSSDK.SimpleEmailV2`, `SesEmailBroker` (no attachments yet) | $0.10 per 1,000 emails; dedicated IP $24.95/month; $200 new-customer credits for 6 months | production-access request to leave the sandbox (U); AWS sign-up/payment from Uzbekistan untested (U) | **Pick for D13** — already wired, cheapest |
| Postmark | transactional email (D13) | P2 | pinned only (`Postmark` `6.4.0`; `Comms/Email/Postmark/` is empty) | Free 100/month; Basic $15/month for 10k, overage $1.80/1k; dedicated IP from $50/month | manual account review of every new account; sends only to verified domains until approved (typically < 24 h) | Alternative if deliverability to Gmail/Mail.ru/Yandex lags on SES (U) |
| Resend | transactional email (D13) | P2 | none | Free 3,000/month, 100/day; Pro $20/month for 50k, overage $0.90/1k | none found | Skip; 100/day free cap |
| Own SMTP / `MailKit` | any relay | P1 dev | wired: `MailKitEmailBroker` (`MailKit` `4.18.0`) | free | the VPS provider's outbound port 25: reports conflict (U); self-run IP reputation | Use only as the relay client; SES, Postmark and Resend all have SMTP endpoints, so the broker already reaches them |
| SendGrid | transactional email | — | wired: `SendGridEmailBroker` | not evaluated | — | Not a D13 candidate |

---

## 4. Social providers

SDK: 17 wrappers wired (`AspNet.Security.OAuth.*` `10.0.0` + Microsoft built-ins), one uniform signature, `wt:provider` stamp. `OpenIddict.Client.WebIntegration` lists all 17 as well (VK as **VK ID**).

| Integration | Purpose | Phase | SDK status | Licence/cost | UZ constraint | Pick |
|---|---|---|---|---|---|---|
| Google | sign-in | P2 | wired (`Microsoft.AspNetCore.Authentication.Google`) | free; `openid email profile` needs no verification; consent screen must be "In production" | highest reach; one client per instance | Ship |
| Apple | sign-in on iOS | P2 | wired (`teamId`, `keyId`, `.p8`) | $99/year Apple Developer Program; Services ID + domain verification | Developer Program availability for an Uzbekistan resident not confirmed (U) | Ship if any iOS app is in scope |
| Facebook | sign-in | P2 | wired | free; `public_profile` + `email` need no app review; business verification only for advanced permissions | usable; mid priority | Optional |
| Microsoft | personal + work accounts | P2 / P3 | wired (`MicrosoftAccount`); `Microsoft.Identity.Web` `3.5.0` pinned only | free | none | Ship; Entra in section 5 |
| GitHub | developer sign-in | P2 | wired | free | none | Ship for dev tools only |
| GitLab | developer sign-in | P2 | wired | free | low relevance | Optional |
| Yandex | sign-in | P2 (spec P3) | wired (`AspNet.Security.OAuth.Yandex`) | free; app type "for user authorization" is fixed at registration; verify the service or users see a warning | strong CIS reach | Ship early |
| VK | sign-in | P2 (spec P3) | wired but **legacy**: `AspNet.Security.OAuth.Vkontakte` targets `oauth.vk.com`; apps created in the VK ID cabinet (`id.vk.ru`) need PKCE + `device_id` | free; app moderation rules not checked (U) | CIS reach; non-standard error responses | Switch to `AspNet.Security.OAuth.VkId` `10.0.0` or OpenIddict's VK ID provider |
| LinkedIn | B2B sign-in | P2 | wired | free; "Sign In with LinkedIn using OpenID Connect" product on an app tied to a LinkedIn Page; approval step sources disagree (U) | low relevance | Optional |
| Discord | sign-in | P2 | wired | free | low relevance | Optional |
| Slack | sign-in | P2 | wired | free | low relevance | Optional |
| Twitch | sign-in | P2 | wired | free | low relevance | Optional |
| Amazon | sign-in | P2 | wired | free; security profile + privacy URL | low relevance | Optional |
| Notion | workspace-based sign-in | P2 | wired | free; integration-based, not a pure identity login | low relevance | Drop from defaults |
| Reddit | sign-in | P2 | wired | **new Data API apps need pre-approval** (Responsible Builder Policy, Nov 2025); approvals reported slow | low relevance | Drop |
| Spotify | sign-in | P2 | wired | **dev mode: 5 users per app, owner needs Premium (Feb 2026); extended mode needs a registered org with ≥250k MAU** | unusable for a public login | Drop |
| X | sign-in | P2 | wired (`AspNet.Security.OAuth.Twitter`) | API moved to pay-per-use in Feb 2026, free tier largely gone; whether login-only calls bill is unconfirmed (U) | low relevance | Drop until priced |
| Generic OIDC | any standards IdP | P2 | wired (`Identity/Oidc`, Authorization Code + PKCE) | free | — | Keep; the service uses `OpenIddict.Client` for runtime issuers |
| Mail.ru ID | CIS email-account sign-in | — | none | `o2.mail.ru` OAuth2 endpoints exist in third-party libs; official 2026 docs not found (U); may be folded into VK ID (U) | CIS reach | Defer |
| OK.ru (Odnoklassniki) | CIS social sign-in | — | none | not checked (U) | CIS reach | Defer |

---

## 5. Enterprise and regional

| Integration | Purpose | Phase | SDK status | Licence/cost | UZ constraint | Pick |
|---|---|---|---|---|---|---|
| Microsoft Entra ID | per-organization work sign-in | P3 | pinned only (`Microsoft.Identity.Web` `3.5.0`; latest 4.16.1) | free; multi-tenant app registration, `/organizations` authority, per-tenant `iss` + `tid` check, admin consent | none | `OpenIddict.Client` Microsoft provider with per-org registration; skip `Microsoft.Identity.Web` |
| Google Workspace | per-organization sign-in | P3 | covered by Google wrapper | free; use `hd` hint + claim check | none | Same Google client, domain-restricted per org |
| Sustainsys.Saml2 | SAML 2.0 service provider | P3 | none | v3: MIT only under `/src/foss`, `/src/commercial` needs a licence agreement; v2.11.0 MIT, net8 asset only, last release 2025-03, fixes only | none | Not first choice |
| ITfoxtec.Identity.Saml2 | SAML 2.0 SP and IdP, metadata, signing, encryption, SLO | P3 | none | BSD-3-Clause; `4.21.0` (2026-09-15); net6–net10 | none | **Pick** for SAML |
| ComponentSpace SAML 2.0 | SAML | P3 | none | commercial, per developer, perpetual (ASP.NET build from $1,273; Core price not found, U); annual licences ended 2026-04 | none | Skip |
| SCIM 2.0 | provisioning from customer IdPs | P3 | none | `Rsk.AspNetCore.Scim` 9.1.0 commercial (price on request, U); `EzSCIM` 0.0.1, `ScimSlim` 0.0.1 (MIT, partial) | none | Build a small RFC 7644 subset (`/Users`, `/Groups`, filter `eq`) |
| OneID (id.egov.uz) | Uzbek e-government sign-in; returns PINFL, passport | P3 | none | state bodies free under Cabinet resolution 679 (2020-10-30); other businesses pay under a contract, fee not published | contract required; endpoint is one `sso.egov.uz/sso/oauth/Authorization.do` URL, not standard OIDC (per a third-party client); sandbox/approval process not found (U) | Defer to a named consumer; bespoke adapter |
| Yandex ID | CIS sign-in | P3 | wired | see section 4 | — | Move to P2 |

---

## 6. Supporting infra

| Integration | Purpose | Phase | SDK status | Licence/cost | UZ constraint | Pick |
|---|---|---|---|---|---|---|
| DB-IP Lite (MMDB) | country/city in the sign-in log | P1 | wired: `AddDbIpLiteIpLocation`, monthly download, `MaxMind.Db` `5.2.0` reader | CC BY 4.0, attribution link "IP Geolocation by DB-IP" on pages showing results; monthly; no account | none; accuracy lower than paid | **Pick** (already wired) |
| MaxMind GeoLite2 | same | P1 alt | wired through `AddMmdbIpLocation` | free account + licence key; EULA; delete old databases within 30 days of a new release; 30 downloads/day; City not recommended for commercial use | MaxMind sign-up from UZ untested (U) | Fallback |
| User-agent parsing | device column in sign-in log, session list | P1 | partial: `UserAgentDeviceMapper` heuristics (bot/iOS/Android/desktop only) | `UAParser` 3.1.47 Apache-2.0 (2021-05, regexes stale); `DeviceDetector.NET` 6.5.2 (2026-07) repo says Apache-2.0 but is a port of Matomo's detector, LGPL upstream (U) | none | `UAParser` + current uap-core `regexes.yaml`; avoid `DeviceDetector.NET` until its licence is read (the SDK allows LGPL only for Hangfire) |
| Bot protection | sign-up and code-request abuse | P1 limits · P2 CAPTCHA | wired: `Web/Captcha` siteverify broker for Turnstile, hCaptcha, reCAPTCHA | Turnstile free: 20 widgets, 10 hostnames each, unlimited challenges; hCaptcha free tier, Pro $99–139/month | none; both work without the site behind Cloudflare | Turnstile |
| ASP.NET Data Protection key ring | cookies, antiforgery, passkey ceremony state, OTP sealing | P1 | partial: `AddPersistentDataProtection` = file-system XML, **unencrypted**, fails startup without a directory | built in; `PersistKeysToDbContext` + `ProtectKeysWithCertificate`; default key life 90 days | none | Add EF Core key ring in PostgreSQL + certificate protection for multi-instance |
| Signing-key storage | OIDC signing keys | P1 | none (JWT is HS256 only) | RSA/ECDSA via X.509 or `SecurityKey`; D10: host files rendered from Wheelhouse's vault | none | Encrypted keys on host files per D10; rotation job in I17 |
| PostgreSQL | user store, OpenIddict tables, OTP/session stores | P1 | wired: `Npgsql` / `Npgsql.EntityFrameworkCore.PostgreSQL` pinned `10.0.0` (provider's latest stable is 10.0.3), `dbup-postgresql`, `Hangfire.PostgreSql` | PostgreSQL licence; 18.6 current, 14 EOL 2026-11-12; 19 RC1 | data-residency question (see findings) | PostgreSQL 18 |
| OpenID Foundation conformance suite | OP conformance before Form 2 ships | P1 | none | MIT; prebuilt Docker images (server + nginx + MongoDB 6.0.13); release v5.3.3 (2026-10-14); certification $700 (members) / $3,500 (others) per deployment, open source may get a waiver | none; default hostname `localhost.emobix.co.uk` via hosts entry | Run self-hosted in CI; certify only if a customer asks |

---

## Findings that change the spec

- Back-channel logout (P1) is not in OpenIddict 7.7.2; build it on our session table.
- The Ocharo "member removed" flow needs short access tokens plus introspection; OpenIddict does not check revocation on API calls by default.
- Front-channel logout (P2): drop; the OpenIddict maintainer has no plans, and third-party-cookie blocking breaks it.
- OpenIddict is pinned at 6.0.0, out of support; use 7.7.2, which fixes client-assertion audience vulnerability GHSA-925x-4h4v-2792.
- Bumping to OpenIddict 7.7.2 forces EF Core ≥ 10.0.11; the props pin 10.0.3.
- Fido2 3.0.1 predates security fixes in 4.2.0 (SSRF, CBOR DoS, metadata cache poisoning); bump to 4.3.1.
- DPoP and dynamic client registration are not in OpenIddict; neither is in the spec, so none is needed.
- Log in with Telegram has no OpenIddict provider; its discovery is standard, so a generic issuer should work (spike).
- Telegram's OIDC has no userinfo endpoint and no refresh grant; the ID token carries `phone_number`.
- `EdDSA`/`ES256K` bot signing restricts Telegram tokens to the `openid` scope; keep the default `RS256`.
- Telegram's login popup breaks under `Cross-Origin-Opener-Policy: same-origin`; exempt the sign-in pages from that header.
- Telegram Gateway is paid in TON only; for an Uzbekistan operator that makes it a defer, not a default P2.
- Twilio charges $0.4571 per UZ SMS, about 60× Eskiz; for Uzbekistan OTP, Twilio and Vonage are fallbacks only.
- Eskiz needs a contract and serves legal entities and residents of Uzbekistan; the operator entity matters (an open legal-entity decision).
- SDK VK wrapper targets legacy `oauth.vk.com`; new VK ID apps need `AspNet.Security.OAuth.VkId` or OpenIddict's VK ID provider.
- Spotify (5-user dev mode, 250k-MAU extended gate), Reddit (pre-approval) and X (pay-per-use) are unusable for public login; the 17 become 14 or fewer.
- Data Protection keys sit unencrypted on disk; multi-instance P1 needs an EF Core key ring in PostgreSQL plus certificate protection.
- OpenIddict does not rotate signing keys or reload them live; I17 must own rotation.
- OpenIddict.Client's maintainer-suggested path for per-organization providers is options reload or a derived client service; no ready API.
- Telegram `Contact.user_id` is optional and not documented as the sender's id; the spec's sender check is required.
- SAML: ITfoxtec.Identity.Saml2 (BSD-3, net10) is the permissive pick; Sustainsys v3 splits MIT core from commercial parts.
- SCIM has only commercial or toy .NET libraries; build a minimal server in P3.
- zxcvbn: .NET ports are stale; put the meter in the fe SDK on `@zxcvbn-ts/core` and keep be at NIST length + HIBP.
- D13: SES is already wired and cheapest; Postmark has no SDK broker (empty folder) and manually reviews new accounts; every candidate also works over SMTP.
- Uzbek personal-data law: localization (Art. 27-1) was reportedly narrowed in March 2026 to biometric/genetic/telecom data (U); database registration still applies — confirm with Uzbek counsel before hosting the identity DB abroad.
- OneID is contract-based, non-OIDC, returns PINFL and passport data: P3 stays, on a named consumer only.
- The conformance suite is MIT and self-hostable in CI via prebuilt images; the spec's "runs in CI before Form 2 ships" is feasible.
- OpenIddict majors ship about yearly with schema changes; 8.0 previews add `Session`/`Resource` tables.

---

## Sources

- https://github.com/openiddict/openiddict-core — Apache-2.0, supported flows (device, token exchange), sponsorship
- https://github.com/openiddict/openiddict-core/releases — 7.7.2, 7.7.0 security fix, 8.0 previews and breaking changes
- https://www.nuget.org/packages/OpenIddict.AspNetCore — 7.7.2 stable, net10.0, 2026-10-10
- https://www.nuget.org/packages/OpenIddict.EntityFrameworkCore/7.7.2 — EF Core Relational ≥ 10.0.11 on net10.0
- https://www.nuget.org/packages/OpenIddict.Client.WebIntegration/7.7.2 — client web integration, no Telegram mention
- https://documentation.openiddict.com/integrations/web-providers — provider list (VK ID, Yandex, no Telegram, no Mail.ru)
- https://documentation.openiddict.com/configuration/application-permissions — endpoint and grant permissions (PAR, introspection, revocation, end session)
- https://documentation.openiddict.com/configuration/encryption-and-signing-credentials — credentials, rollover by adding certificates
- https://documentation.openiddict.com/configuration/authorization-storage — pruning, revocation limits, `EnableAuthorizationEntryValidation`
- https://documentation.openiddict.com/guides/migration/60-to-70 — 7.0 token exchange, schema change, framework support
- https://kevinchalet.com/2025/07/07/openiddict-7-0-is-out/ — 7.0 features
- https://github.com/openiddict/openiddict-core/issues/2175 — back-channel logout plan, front-channel "no plans"
- https://github.com/openiddict/openiddict-core/issues/2483 — DPoP unsupported, mTLS preferred
- https://github.com/openiddict/openiddict-core/issues/2404 — dynamic client registration, milestone slips
- https://github.com/openiddict/openiddict-core/issues/2273 — runtime per-tenant client registrations
- https://github.com/openiddict/openiddict-core/issues?q=%22token+exchange%22 — token exchange merged 2025-06-03
- https://github.com/openiddict/openiddict-core/blob/dev/src/OpenIddict.Server/OpenIddictServerHandlers.Protection.cs — redeemed-token replay revokes the chain (read via raw source, `dev` branch)
- https://github.com/openiddict/openiddict-core/blob/dev/src/OpenIddict.Server/OpenIddictServerBuilder.cs — `AllowTokenExchangeFlow`, `AllowDeviceAuthorizationFlow`, PAR, `SetRefreshTokenReuseLeeway`
- https://openiddict.com/support — only latest stable supported; sponsor extended support
- https://core.telegram.org/bots/telegram-login — OIDC flow, scopes, BotFather, signing algorithms, COOP note
- https://oauth.telegram.org/.well-known/openid-configuration — Telegram discovery document
- https://core.telegram.org/widgets/login — legacy widget archived, OIDC is the current path
- https://core.telegram.org/bots/api — Bot API 10.3, `request_contact`, `Contact.user_id`, `secret_token`
- https://core.telegram.org/bots/faq — rate limits
- https://core.telegram.org/gateway — $0.01 per delivered code, refunds, opt-in
- https://core.telegram.org/gateway/api — methods, TTL, callback signature, own-number free
- https://gateway.telegram.org/ — payment in TON
- https://www.twilio.com/en-us/sms/pricing/uz — $0.4571 per UZ SMS, sender ids
- https://sent.dm/resources/uzbekistan-sms-pricing — UZ SMS market notes (secondary, 2025)
- https://www.bitrix24.ru/apps/app/vebpult.eskiz_sms_shlyuz_uzbekistan/ — Eskiz 85 UZS, contract, legal entities and residents (secondary)
- https://packagist.org/packages/uzbek/laravel-sms — Eskiz, PlayMobile, TextUp, Sayqal driver notes (secondary)
- https://developers.facebook.com/documentation/business-messaging/whatsapp/pricing.md — per-message pricing, UZ in Rest of Asia Pacific
- https://whautomate.com/tools/whatsapp-business-api-pricing-calculator — "Other" authentication rate (reseller, not used for UZ)
- https://aws.amazon.com/ses/pricing/ — SES prices
- https://postmarkapp.com/pricing — Postmark plans
- https://postmarkapp.com/support/article/1084-how-does-the-account-approval-process-work — manual account review (search result)
- https://resend.com/pricing.md — Resend plans
- https://www.nuget.org/packages/Fido2.AspNet and https://github.com/passwordless-lib/fido2-net-lib/releases — 4.3.1, security fixes, breaking changes
- https://www.nuget.org/packages/Konscious.Security.Cryptography.Argon2 — 1.3.1, 2024-06-19
- https://www.nuget.org/packages/Otp.NET — 1.4.1
- https://haveibeenpwned.com/API/v3 — Pwned Passwords no key, no rate limit
- https://azuresearch-usnc.nuget.org/query?q=zxcvbn — .NET zxcvbn packages and ages; https://registry.npmjs.org/@zxcvbn-ts/core — 4.2.0
- https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/releases and https://www.nuget.org/packages/AspNet.Security.OAuth.VKID — VkId package, 10.0.0, "use OpenIddict client for new applications"
- https://developer.spotify.com/documentation/web-api/tutorials/february-2026-migration-guide — dev mode limits (search result)
- https://r.datuan.dev/r/redditdev/comments/1oug31u/introducing_the_responsible_builder_policy_new/nogle9d/ — Reddit approval (mirror, search result)
- https://gigazine.net/gsc_news/en/20260209-x-api-pay-per-use — X pay-per-use (search result)
- https://developers.facebook.com/documentation/facebook-login/login-connect — Facebook review rules (search result)
- https://learn.microsoft.com/en-us/linkedin/consumer/integrations/self-serve/sign-in-with-linkedin-v2 — LinkedIn OIDC product (search result)
- https://developer.apple.com/support/enrollment/ — $99/year, regional availability caveat
- https://yandex.com/dev/id/doc/en/register-client — Yandex app types, verification (search result)
- https://learn.microsoft.com/en-us/entra/identity-platform/howto-convert-app-to-be-multi-tenant — multi-tenant authority, issuer and consent
- https://raw.githubusercontent.com/Sustainsys/Saml2/main/LICENSE.txt and https://www.nuget.org/packages/Sustainsys.Saml2.AspNetCore2 — Sustainsys licence split, 2.11.0
- https://www.nuget.org/packages/ITfoxtec.Identity.Saml2.MvcCore and https://github.com/ITfoxtec/ITfoxtec.Identity.Saml2 — BSD-3, 4.21.0, .NET 10
- https://www.componentsource.com/product/componentspace-saml2-core/prices/buy/0719-1891cf — ComponentSpace licensing (search result)
- https://www.nuget.org/packages/Rsk.AspNetCore.Scim — commercial SCIM (search result)
- https://egov.uz/en/projects/one-id — OneID contract terms, resolution 679
- https://packagist.org/packages/aslnbxrz/oneid-socialite — OneID endpoint and fields (third party)
- https://www.legal500.com/developments/thought-leadership/personal-data-compliance-in-uzbekistan/ — Art. 27-1 localization, database registration
- https://kun.uz/en/news/2026/03/27/uzbekistan-amends-personal-data-law-to-facilitate-global-payment-systems — March 2026 amendment (headline and search summary only; U)
- https://db-ip.com/db/lite.php — DB-IP Lite licence
- https://dev.maxmind.com/geoip/geolite2-free-geolocation-data/ — GeoLite2 terms
- https://github.com/totpero/DeviceDetector.NET and https://www.nuget.org/packages/UAParser — UA parsers
- https://developers.cloudflare.com/turnstile/plans/ and https://www.hcaptcha.com/pricing — CAPTCHA plans
- https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0 — key ring in a database, certificate protection, 90-day default
- https://www.postgresql.org/support/versioning/ — PostgreSQL 18.6, EOL dates
- https://gitlab.com/openid/conformance-suite and its wiki Developers/Build-&-Run (GitLab API) — MIT, prebuilt Docker, v5.3.3, `.gitlab-ci/run-tests.sh`
- https://openid.net/certification/fees/ — certification fees

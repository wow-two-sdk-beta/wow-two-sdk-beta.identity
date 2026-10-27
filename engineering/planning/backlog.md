# wow-two-sdk-beta.identity — Backlog

*Last updated: 2026-10-27*

Every unbuilt item, grouped; top of each group = next. Shipped work lives in [`version-track/`](version-track/).
Shape: wow-two-ws/conventions/planning/version-track/version-track.md

## Features

| Feature | State | Spec |
|---|---|---|
| Sign-in methods | partial: SDK flows exist, no hosted pages; P1 bot + Telegram planned | [sign-in-methods.md](../../product/features/sign-in-methods.md) |
| Accounts and members | partial: organizations and memberships exist; invitations planned | [accounts.md](../../product/features/accounts.md) |
| Sessions and tokens | partial: refresh rotation exists; OIDC server, keys, logout planned | [sessions.md](../../product/features/sessions.md) |
| Abuse protection | partial: fixed lockout exists; durable codes and limits planned | [abuse-protection.md](../../product/features/abuse-protection.md) |
| Admin console | planned | [admin-console.md](../../product/features/admin-console.md) |
| Member self-service | planned | [self-service.md](../../product/features/self-service.md) |
| Developer surface | partial: fe auth exists; Client and `fe-identity` screens planned | [developer-surface.md](../../product/features/developer-surface.md) |
| Second factors and step-up | partial: TOTP, passkeys, recovery codes exist | [second-factors.md](../../product/features/second-factors.md) |
| Passwords | partial: Argon2id and breached check exist; policies planned | [passwords.md](../../product/features/passwords.md) |

## P1 — Ocharo slice

| Item | Type | Notes |
|---|---|---|
| I14 invitations and admin-created members | feature | invite links with a role and a per-link expiry and use limit (one, a number, none); phone or email pre-registration; accepted on sign-in |
| I15 durable OTP store | feature | hashed at rest, shared by instances, limits per phone, IP and client |
| I16 sessions and sign-in log | feature | a session per device, revoke one or all, every attempt recorded |
| I17 signing keys and JWKS | feature | key-source seam, startup validation, rotation with overlap |
| I18 OIDC server | feature | authorize, token, end-session, discovery, back-channel logout, client registry |
| I19 OIDC client | feature | the `Client` package: cookie session, organization claims, logout receiver |
| I20 Telegram | feature | link on first sign-in; Log in with Telegram with the `phone` scope |
| test mode | feature | fake channels, code sink, personas, refused in production |
| hosted pages and console | feature | `apps/web` over `fe-identity` (`./kit`, `./admin`) |
| conformance in CI | feature | OpenID Foundation suite against the image in `Testing` mode |

## P2 — the whole vector

| Item | Type | Notes |
|---|---|---|
| I21 admin API | feature | members, sessions, sign-in log, registered apps |
| I22 password lifecycle | feature | policies as settings, forced reset, temporary passwords, generator |
| more channels and methods | feature | Gateway, SMS, email, passwords, passkeys, social providers |
| management API and webhooks | feature | scoped keys; `member.added`, `signed_in` |

## Groundwork

| Item | Type | Notes |
|---|---|---|
| migrator follow-ups | chore | owner marker names the instance, baseline, multi-set test harness |
| claim vocabulary | chore | converge on `sub`, `sid`, `org_id` before the Client ships |
| dependency bumps | chore | OpenIddict 7.7.2 with EF Core ≥ 10.0.11; Fido2 4.3.1 |

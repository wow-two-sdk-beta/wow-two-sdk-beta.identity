# wow-two-sdk-beta.identity — Backlog

*Last updated: 2026-11-01*

Every unbuilt item, grouped; top of each group = next. Shipped work lives in [`version-track/`](version-track/).
Shape: wow-two-ws/conventions/planning/version-track/version-track.md

## Features

| Feature | State | Spec |
|---|---|---|
| Sign-in methods | partial: SDK flows exist, no hosted pages; P1 bot + Telegram planned | [sign-in-methods.md](../../product/features/sign-in-methods.md) |
| Accounts and members | partial: organizations and memberships exist; invitations planned | [accounts.md](../../product/features/accounts.md) |
| Sessions and tokens | partial: OIDC server, keys, sessions, revocation and sign-in log exist; back-channel logout planned | [sessions.md](../../product/features/sessions.md) |
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
| I18 back-channel logout | feature | member removal signs every app out at once |
| app access check | feature | refuse sign-in and tokens for an app missing from the member's `app_access` (Q61) |
| `amr` after a second factor | fix | keep `pwd` beside `otp` and `mfa` once a password sign-in passes TOTP (RFC 8176) |
| I19 OIDC client | feature | the `Client` package: cookie session, organization claims, logout receiver |
| I20 Telegram | feature | link on first sign-in; Log in with Telegram with the `phone` scope |
| test mode | feature | fake channels, code sink, personas, refused in production |
| hosted pages and console | feature | `apps/web` over `fe-identity` (`./kit`, `./admin`); sign-in passes `?clientId=` for the sign-in log; sign-up shows only while `Identity:SignUp:Enabled` is on, read at runtime |
| console layout review | product | with the developer once Ocharo's capabilities are verified; the console starts on Wheelhouse's layout |
| Ocharo's instance as Ocharo's service | product | the image pinned by tag in Ocharo's `deploy.yml`, its first `migrate:` item when `access.*` moves (Q66); Wheelhouse v0.11 generates its keys and links the client secret (`deployment.md` § Pinning the instance) |
| generated keys in its own `deploy.yml` | feature | `generate` on the three certificates and `from` on the issuer once pipelines `v0.14.0` ships, for wow2's shared instance |
| client credentials and API scopes | feature | service calls that carry the actor, with audiences or scopes beyond one `aud` per client (G8, G10); Ocharo P1 |
| agent tokens | feature | a person issues a revocable token to an agent, bound to their membership: OAuth for MCP or personal access tokens; Ocharo P1 |
| conformance in CI | feature | OpenID Foundation suite against the image in `Testing` mode |
| broker conformance | feature | the module, the client and the fake pass the same broker cases |

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
| channels from the communications library | engineering | email and Telegram delivery, wired in the host for v0.4, move to the communications library once it ships |
| service verbs from the be sdk | engineering | `keys`, `migrate` and `config check` dispatch through the be sdk's verb host, so `Program.cs` stays thin |
| Fido2 upgrade | engineering | Fido2 4.3.1: passkey transports and backup state |
| instance composition in a package | engineering | one call holding what `service/Identity.Api` wires, so an own host stays thin ([own hosts](../architecture/architecture.md#own-hosts)) |
| host CLI in a package | engineering | `admin create` and a one-time sign-in beside `keys generate` |
| require `sid` on cookies | fix | refuse a cookie without `sid` once every sign-in path starts a session |
| prune ended sessions | engineering | delete revoked and expired `identity_sessions` rows past a window, as the sign-in log does |
| database ring in the be sdk | engineering | move the certificate-wrapped database ring from Server to core, so every product gets it |
| sign-in log partitions | engineering | monthly partitions once the volume asks for them |

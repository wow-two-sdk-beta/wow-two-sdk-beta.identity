# wow-two-sdk-beta.identity — Backlog

*Last updated: 2026-11-02*

Every unbuilt item, grouped; top of each group = next. Shipped work lives in [`version-track/`](version-track/).
Shape: wow-two-ws/conventions/planning/version-track/version-track.md

## Features

| Feature | State | Spec |
|---|---|---|
| Sign-in methods | partial: password with email confirmation, phone by Telegram, hosted pages and the declaration ship; open sign-up's email-first form planned | [sign-in-methods.md](../../product/features/sign-in-methods.md) |
| Accounts and members | partial: organizations, the fixed role catalog, invitation links, member routes, the members panel and the invite sign-up with name, picture and a bot-verified phone ship | [accounts.md](../../product/features/accounts.md) |
| Sessions and tokens | partial: OIDC server, keys, sessions with fixed, extended or sliding lifetimes, revocation, the BFF and sign-in log ship; back-channel sign-out planned | [sessions.md](../../product/features/sessions.md) |
| Abuse protection | partial: lockout, durable hashed codes, a per-number failure budget and per-address rate limits ship | [abuse-protection.md](../../product/features/abuse-protection.md) |
| Admin console | planned | [admin-console.md](../../product/features/admin-console.md) |
| Member self-service | planned | [self-service.md](../../product/features/self-service.md) |
| Developer surface | partial: the `Client` BFF and `fe-identity` `./kit` and `./admin` ship | [developer-surface.md](../../product/features/developer-surface.md) |
| Second factors and step-up | partial: TOTP, passkeys, recovery codes exist | [second-factors.md](../../product/features/second-factors.md) |
| Passwords | partial: Argon2id and breached check exist; policies planned | [passwords.md](../../product/features/passwords.md) |

## P1 — Ocharo slice

| Item | Type | Notes |
|---|---|---|
| `admin create` and admin recovery | analysis | what the host CLI should create (developer, 2026-11-02): only the instance admin, and only once? today it makes the first organization owner, and `--role admin` also grants the instance admin; how a new admin is made when the old one loses access; other edge cases (a lost phone, a hijacked Telegram account, the last owner leaving); would revise the decision rows 2026-11-01 "the host CLI creates the first admin only", 2026-11-01 `admin create` exits, 2026-11-02 instance roles and 2026-11-02 invitations in the console only |
| trusted devices and contacts | feature | sign in by sending the code to someone else: a person names trusted contacts (or devices) who may receive their code, for when their own phone is out of reach (developer, 2026-11-02) |
| open sign-up | feature | C13: the person joins as a guest (no organization role) through an email-first form that turns into sign-in or sign-up |
| hosted pages layout | product | C14: what the developer names; with the console layout review below |
| invitation use limits | feature | a link for a number of people or none, beside today's single use; accept after signing in from the invitation page |
| I18 back-channel sign-out | feature | member removal signs every app out at once |
| app access check | feature | refuse sign-in and tokens for an app missing from the member's `app_access` (Q61) |
| `amr` after a second factor | fix | keep `pwd` beside `otp` and `mfa` once a password sign-in passes TOTP (RFC 8176) |
| BFF sign-out receiver | feature | the `Client` package takes back-channel sign-out (I18) |
| I20 Telegram | feature | link on first sign-in; Sign in with Telegram with the `phone` scope |
| test kit | feature | fakes, personas and a test-host helper in the `Testing` package and test projects; the runtime ships no test mode (2026-11-01) |
| hosted pages and console | feature | `apps/web` over `fe-identity` (`./kit`, `./admin`); sign-in passes `?clientId=` for the sign-in log; sign-up shows only in `Identity:SignUp:Mode=Open` or from an invitation link, read at runtime; the console signs in by its own cookie through Wheelhouse's one-time links (C4, open) |
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
| custom sign-up fields | feature | after G3: any profile field with its type, rules and labels in `Identity:SignUp:Profile`, beyond today's name and picture (developer, 2026-11-02) |
| profile self-service | feature | after G3: a person edits their name and picture from the account page |
| pictures on File Storage | feature | after G3: an `IProfilePictureStore` adapter over File Storage, with image variants; the database store stays the default |
| Telegram photo prefill | feature | after G3: the bot offers the person's Telegram profile photo as the sign-up picture |
| I22 password lifecycle | feature | policies as settings, forced reset, temporary passwords, generator |
| more channels and methods | feature | Gateway, SMS, email, passwords, passkeys, social providers |
| management API and webhooks | feature | scoped keys; `member.added`, `signed_in` |

## Groundwork

| Item | Type | Notes |
|---|---|---|
| channels from the communications library | engineering | email and Telegram delivery, wired in the host for v0.4, move to the communications library once it ships |
| Fido2 upgrade | engineering | Fido2 4.3.1: passkey transports and backup state |
| instance composition in a package | engineering | one call holding what `service/Identity.Api` wires, so an own host stays thin ([own hosts](../architecture/architecture.md#own-hosts)) |
| host CLI in a package | engineering | `admin create` and a one-time sign-in beside `keys generate` |
| require `sid` on cookies | fix | refuse a cookie without `sid` once every sign-in path starts a session |
| BFF session store | feature | a built-in shared store and key ring for the BFF, so a product needs no wiring of its own to survive deploys (bug sweep #5) |
| absolute cap for sliding sessions | feature | an optional end no use moves, beside `SlidingLifetime` |
| rename `SessionOptions` | engineering | it clashes with ASP.NET Core's `SessionOptions` wherever `Microsoft.AspNetCore.Builder` is imported |
| phone `send` answers alike | fix | `phone-sign-in/send` still tells a linked number apart (bug sweep #8, accepted for now) |
| prune ended sessions | engineering | delete revoked and expired `identity_sessions` rows past a window, as the sign-in log does |
| database ring in the be sdk | engineering | move the certificate-wrapped database ring from Server to core, so every product gets it |
| sign-in log partitions | engineering | monthly partitions once the volume asks for them |

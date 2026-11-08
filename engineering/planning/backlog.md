# wow-two-sdk-beta.identity — Backlog

*Last updated: 2026-11-08*

Every unbuilt item, grouped; top of each group = next. Shipped work lives in [`version-track/`](version-track/).
Shape: wow-two-ws/conventions/planning/version-track/version-track.md

## Features

| Feature | State | Spec |
|---|---|---|
| Sign-in methods | partial: password with email confirmation, phone by Telegram, hosted pages and the declaration ship; open sign-up's email-first form planned | [sign-in-methods.md](../../product/features/sign-in-methods.md) |
| Accounts and members | partial: organizations, the fixed role catalog, invitation links, member routes, the members panel and the invite sign-up with name, picture and a bot-verified phone ship | [accounts.md](../../product/features/accounts.md) |
| Sessions and tokens | partial: OIDC server, keys, sessions with fixed, extended or sliding lifetimes, revocation, the BFF and sign-in log ship; revocation signals in v0.5 iteration 12 | [sessions.md](../../product/features/sessions.md) |
| Abuse protection | partial: lockout, durable hashed codes, a per-number failure budget and per-address rate limits ship | [abuse-protection.md](../../product/features/abuse-protection.md) |
| Admin console | planned | [admin-console.md](../../product/features/admin-console.md) |
| Member self-service | planned | [self-service.md](../../product/features/self-service.md) |
| Developer surface | partial: the `Client` BFF and `fe-identity` `./kit` and `./admin` ship | [developer-surface.md](../../product/features/developer-surface.md) |
| Second factors and step-up | partial: TOTP, passkeys, recovery codes exist | [second-factors.md](../../product/features/second-factors.md) |
| Passwords | partial: Argon2id and breached check exist; policies planned | [passwords.md](../../product/features/passwords.md) |

## P1 — Ocharo slice

| Item | Type | Notes |
|---|---|---|
| admin recovery and account edge cases | analysis | `admin create` now makes only the instance admin, once (2026-11-03); parked (developer, 2026-11-08: "yes park it"): how a new admin is made when the old one loses access; still open, other edge cases (a lost phone, a hijacked Telegram account or a recycled SIM logging into the old Telegram account, a person in no organization whose Telegram change nobody can approve); would revise the decision rows 2026-11-01 "the host CLI creates the first admin only", 2026-11-01 `admin create` exits, 2026-11-02 instance roles and 2026-11-02 invitations in the console only |
| trusted devices and contacts | feature | slice 1 in v0.5 iteration 10 ([spec](../../product/features/trusted-contacts.md)); left: a person listing and forgetting known devices; revoking a contact ending bearer sign-ins made through them; the contact mark surviving a second factor; the contact's bot message in the account's language |
| open sign-up | feature | C13: the person joins as a guest (no organization role) through an email-first form that turns into sign-in or sign-up |
| hosted pages layout | product | C14: what the developer names; with the console layout review below |
| code input as boxes | product | every code form takes the fe sdk's `PinInput`, one box per digit, instead of one text field: `PhoneCodeSignIn`, `InviteSignUpForm`, `SecondFactorForm` (TOTP; recovery codes stay text) and `ChangePasswordForm`; the pinned `@wow-two-beta/fe` 0.0.40 ships it (developer, 2026-11-03) |
| invitation use limits | feature | a link for a number of people or none, beside today's single use; accept after signing in from the invitation page |
| app access check | feature | refuse sign-in and tokens for an app missing from the member's `app_access` (Q61) |
| `amr` after a second factor | fix | keep `pwd` beside `otp` and `mfa` once a password sign-in passes TOTP (RFC 8176) |
| I20 Telegram | feature | link on first sign-in; Sign in with Telegram with the `phone` scope |
| test kit | feature | fakes, personas and a test-host helper in the `Testing` package and test projects; the runtime ships no test mode (2026-11-01) |
| hosted pages and console | feature | `apps/web` over `fe-identity` (`./kit`, `./admin`); sign-in passes `?clientId=` for the sign-in log; sign-up shows only in `Identity:SignUp:Mode=Open` or from an invitation link, read at runtime; the console signs in by its own cookie through Wheelhouse's one-time links (C4, open) |
| console layout review | product | with the developer once Ocharo's capabilities are verified; the console starts on Wheelhouse's layout |
| Ocharo's instance as Ocharo's service | product | the image pinned by tag in Ocharo's `deploy.yml`, its first `migrate:` item when `access.*` moves (Q66); Wheelhouse v0.11 generates its keys and links the client secret (`deployment.md` § Pinning the instance) |
| generated keys in its own `deploy.yml` | feature | `generate` on the three certificates and `from` on the issuer once pipelines `v0.14.0` ships, for wow2's shared instance |
| revocation signals | feature | v0.5 iteration 12 ([spec](../../product/features/revocation-signals.md)); left: services adopt the receiver and the deny list; a broker for jobs on other replicas |
| service tokens | feature | v0.5 iteration 11 ([spec](../../product/features/service-tokens.md)); left: Desk, Editorial and Files adopt it (G8, G10); Wheelhouse generating client key pairs |
| agent tokens | feature | a person issues a revocable token to an agent, bound to their membership: OAuth for MCP or personal access tokens; Ocharo P1 |
| conformance in CI | feature | OpenID Foundation suite against the image in `Testing` mode |
| broker conformance | feature | the module, the client and the fake pass the same broker cases |

## P2 — the whole vector

| Item | Type | Notes |
|---|---|---|
| Files: API key administration | feature | `files-integration #16` and `files-integration #9`: a port to create (the secret shown once, the record naming the app, organization and partition its claims carry), list, revoke and rotate a key with an overlap window, a mappable endpoint group over it, and a fake key in `Testing`; Files implements the store over its tenants |
| Files: signing keys, the whole vector | feature | `files-integration #14`: ES256 beside RS256; a validator's JWKS cache and refresh as settings on `JwtOptions` and `TrustedIssuer` (12 hours and 5 minutes today, fixed); rotation run by Wheelhouse on the 90-day cadence, the wheelhouse lane's side |
| Files: per-tenant console sign-in | feature | `files-integration #17`: `IOidcSchemeResolver` in `Client` picks the authority, client and secret per tenant at runtime, with BFF sessions keyed by issuer. P1's operator sign-in needs no Identity change under either answer to `#29`: the BFF as built against WoW2's instance, or Wheelhouse one-time links with no instance |
| Files: guest email proof | feature | `files-integration #18`: a person proven by an email code with no organization (`email_verified`, no `org_id`), only for clients with `AllowGuests`, upgradable to a member; lands with open sign-up's email-first form; the share sign-in gate rides the per-tenant schemes above |
| Files: lifecycle events | feature | `files-integration #19`: Files' P2 asks are `OrganizationDeleted` and an erasure event apart from `Removed`, with completion acks beside `IAccountDeletionHandler`; `person.disabled` dropped from Files' needs. The vector adds an account suspension that ends sessions and signals (only lockout exists, which does neither). Not revocation, so it waits for Events, after Files |
| I21 admin API | feature | members, sessions, sign-in log, registered apps; `files-integration #21`: clients (create, rotate a secret, grants), resources and signal URLs over a store-backed registry seeded from configuration, which Wheelhouse drives for tenant onboarding on B; P1 onboarding stays configuration |
| role permissions in the console | feature | an admin changes what each organization role may do, per app and per action, at runtime; today `Identity:Organizations:Roles:{role}` adds namespaced permissions (`desk:orders:edit`) instance-wide from deploy config; the role catalog stays fixed, so only the 2026-11-01 C3 row's "settings add permissions only" changes (developer, 2026-11-03) |
| custom sign-up fields | feature | after G3: any profile field with its type, rules and labels in `Identity:SignUp:Profile`, beyond today's name and picture (developer, 2026-11-02) |
| profile self-service | feature | after G3: a person edits their name and picture from the account page |
| pictures on File Storage | feature | after G3: an `IProfilePictureStore` adapter over File Storage, with image variants; the database store stays the default; `files-integration #20`: the port moves from `Module` to `Abstractions`, so Files' adapter references the contract only, and gains a URL read by size |
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

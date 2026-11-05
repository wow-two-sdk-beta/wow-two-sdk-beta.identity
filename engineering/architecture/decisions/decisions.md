# decisions/

*Last updated: 2026-11-05*

> Identity's confirmed architecture decisions, newest last; a decision that needs its own record becomes
> `NNNN-{slug}.md` beside this doc. Decisions spanning capabilities: `wow-two-ws/docs/platform-decisions.md`.
> Q-numbers point at the [identity analysis](../../research/identity-analysis-2026-10/identity-analysis-2026-10.md).

| Date | Decision | Source |
|---|---|---|
| 2026-10-21 | OpenIddict carries the OAuth 2.0 / OIDC protocol, on our own user store | D1 |
| 2026-10-21 | one person per instance; roles per organization, app permissions as claims | D3 |
| 2026-10-25 | every code channel is configuration — own bot, Telegram Gateway, SMS, email — worded by `IOtpMessageFormatter` in `uz`, `ru`, `en` | D4, Q12 |
| 2026-10-25 | Identity leaves the be sdk for its own domain repo (be + fe library, service, image) | Q1 |
| 2026-10-25 | shared seams live in the kernel `WoW2.Sdk.Beta.Be.Abstractions` in the core repo; core never references Identity | Q1b |
| 2026-10-25 | P1 adds test mode with fake channels, an operator passkey or TOTP, a claims-contributor hook, OpenTelemetry and health | Q18 |
| 2026-10-25 | every password policy is configurable, all off by default; minimum length 15 | Q20 |
| 2026-10-25 | the spec, state and analysis live in this repo; the hub keeps the catalog entry, the platform-apps row and cross-capability decisions | Q36 |
| 2026-10-26 | the repo is `wow-two-sdk-beta.identity` | Q1a |
| 2026-10-26 | three forms under one broker contract; seven packages: Abstractions, Auth, Module, Client, Providers, Server, Testing | Q1c |
| 2026-10-26 | package versions come from tags with no release commit; a nightly job tests against the newest core | Q1d, Q37, Q38 |
| 2026-10-26 | `@wow-two-beta/fe-identity` ships embeddable components and widgets and can forward a flow to the hosted pages; `auth` stays in `@wow-two-beta/fe` | Q1e |
| 2026-10-26 | a Form 1 → 3 move swaps the Module package for the Client, whose brokers delegate to the running server | Q3 |
| 2026-10-26 | the whole identity schema is always created; configuration switches only reads, writes, jobs and seeds | Q46 |
| 2026-10-26 | one SQL migration set in the Module, run by the be sdk migrator; EF only maps | Q47, Q48 |
| 2026-10-26 | `uuid` keys made as Guid v7 in code; no `TKey` / `TUser` generics on stored entities | Q49 |
| 2026-10-26 | extension through side tables keyed by `sub` and claims contributors; `attributes` documents are the narrow exception | Q50 |
| 2026-10-26 | feature switches are configuration only in every form; the Module registers every slice | Q51 |
| 2026-10-26 | schema `identity` plus the `identity_` table prefix | Q52 |
| 2026-10-26 | never two instances on one identity schema; a database per instance by default, a schema per instance allowed | Q53 |
| 2026-10-26 | one instance per isolation boundary: owner, product or residency (widens D2) | Q54 |
| 2026-10-26 | several phones per person in one table, one primary | Q56 |
| 2026-10-26 | production migrations run as an explicit `migrate` deploy step; apply-on-boot in development only | Q57 |
| 2026-10-26 | breaking changes are allowed in the SDKs and products while the products have no users | developer |
| 2026-10-26 | management screens ship in `@wow-two-beta/fe-identity`'s `./admin` export; `apps/web` composes them into the console, and a central services console or a product's admin page imports the same export | developer |
| 2026-10-27 | PostgreSQL only: the Module's SQLite dialect retires, as SQLite is banned for products (replaces Q58) | developer |
| 2026-10-27 | the default image (`service/Identity.Api`) is the general instance any owner pulls and configures; an owner who needs more composes the packages in a host of its own | developer |
| 2026-10-27 | wow2's own instance gets its own host image on the published packages, in a platform repository, later; the default image keeps serving Ocharo meanwhile | developer |
| 2026-10-31 | the service and its image stay in this repo beside the libraries; an instance runs as one container; no move to `wow-two-platform` | developer |
| 2026-10-27 | key and ring certificates are vault secrets as inline PEM (a file path still works); no key file sits on a volume | Q25, Q26 |
| 2026-10-27 | the Data Protection ring lives in `identity_data_protection_keys`, each key wrapped by the active ring certificate; a deployed boot refuses none | Q26 |
| 2026-10-27 | `sid` rides in the ID and access tokens, not userinfo; each grant hangs off an authorization naming the session | spine build |
| 2026-10-27 | a cookie without `sid` passes the session check while other sign-in paths start none; the stamp check still covers it | spine build |
| 2026-10-27 | the sign-in log keeps no `country` or `organization_id` yet; a second factor logs as `2fa:{method}` | spine build |
| 2026-10-27 | SQLite data is not carried to PostgreSQL; an instance that ran on SQLite starts empty | spine build |
| 2026-10-28 | the image's `migrate` is the be sdk verb tree; apply-on-boot stays for local runs only | migrations M10, M11, M57 |
| 2026-10-28 | products may key into the published `identity_users` and `identity_organizations` inside one database; they map them read-only and write through the module or the API (reverses T10's ban) | migrations M5, M7, M79 |
| 2026-10-28 | deleting a person soft-deletes the row; erasure scrubs personal columns in place, so a product's key never blocks | migrations M5, M75 |
| 2026-10-28 | published tables, keys and readable columns change by expand and contract only; a contract file is headed `-- @contract since=vX.Y` | migrations M5, M21, M76 |
| 2026-10-28 | PostgreSQL 18 everywhere: compose, CI and test containers | owner, migrations M65 |
| 2026-10-28 | EF migrations, `EnsureCreated` and `Database.Migrate()` are banned; test databases take the schema from the set's SQL | owner |
| 2026-10-28 | the set is rewritten in place while no database keeps data: soft delete folds into `001-accounts`, every file is idempotent; once one keeps data, applied files are never edited | owner, migrations M5 |
| 2026-10-28 | a per-product instance (Ocharo's) is a service of the product, its set in the product's database, schema `identity`; a shared instance (wow2's) is a Wheelhouse product with its own database (Q5, Q63; replaces Q55's own database) | migrations M6, M77 |
| 2026-10-31 | console users are personal accounts gated by an instance role; no shared account | developer |
| 2026-10-31 | every method, verification and policy is set from `Identity:*` settings (Q51); password rules stay structured fields, never a regex | developer |
| 2026-10-31 | a public declaration names the enabled methods, their fields and their rules; `fe-identity` renders forms and pages from it | developer |
| 2026-10-31 | Ocharo signs in by phone through its own Telegram bot | developer |
| 2026-10-31 | the phone settings name the allowed countries; the fe sdk's `PhoneInput` takes the list | developer |
| 2026-10-31 | settings invalid on their own stop the service; a deployed boot refuses settings that conflict with stored rows, logging `Critical`; `config check` runs after `migrate` and before the restart; a removed method revokes its sessions | developer, C1 |
| 2026-11-01 | every boolean takes an `Is` or `Has` name: members, settings keys, JSON fields and columns; `001-accounts` is rewritten in place and Ocharo dev's identity data is reset | developer, C8 |
| 2026-11-01 | a stricter password rule is checked at the next sign-in once the password matches its hash; a failing one leads to the change form and stays until changed; only invalid settings and stranding are refused (replaces the C1 row's refusal for stored rows) | developer, C1b |
| 2026-11-01 | settings stay the only source; the instance keeps no settings history, and Wheelhouse audits each change | developer, C2 |
| 2026-11-01 | `config check` stays as the boot's own validation run without serving, stateless; it is the pattern for every wow2 service | developer, C9 |
| 2026-11-01 | a password change takes `Identity:Password:Change:Confirmation` = `None` (default), `Code` or `Link`, sent over any verified channel and enforced by the API; `IsSignInAgainRequired` (off by default) ends the current session too | developer, C10 |
| 2026-11-01 | the declaration lists each method's fields and rules as fe sdk rule codes with messages in the request's locale, at a public `GET /api/account/sign-in-options`; `?clientId=` returns that app's methods, theme and background; `fe-identity` builds one component per method | developer, C5–C7 |
| 2026-11-01 | from 0.3.7 on the migration set is forward-only: Ocharo resets dev and prod once for the in-place `001-accounts`, then no applied file changes | Ocharo |
| 2026-11-01 | only the instance reads its Telegram bot's updates: a webhook when hosted, polling on a local run | developer, Ocharo |
| 2026-11-01 | one bot per environment serves a whole product (Ocharo: Desk and the platform) — prod and dev, no local bot for now (a local run polls only while no hosted webhook holds the dev bot); the instance reads its updates, other services may only send through it | developer, Ocharo |
| 2026-11-01 | the hosted pages take one look per instance (`Identity:Pages:{BrandName,Theme,BackgroundUrl}`), as an instance serves one product; no per-client look | developer |
| 2026-11-01 | no test mode, fake channel or message sink in the runtime; dev and local flows use real dev accounts and bots; fakes live in test projects | developer |
| 2026-11-01 | every route and member says sign in, sign up and sign out: `/api/account/{sign-in,sign-up,sign-out}`, a BFF's `/api/identity/{session,sign-in,sign-out,callback}` | developer |
| 2026-11-01 | the host CLI creates the first admin only (`admin create`); members are managed elsewhere (a 2026-11-02 widening by `admin invite` was reversed the same day) | developer |
| 2026-11-01 | the flows other apps need ship and deploy first, then Ocharo goes live on them; the console comes after | developer |
| 2026-11-01 | a fixed role catalog: instance `admin`, `support`, `auditor`; organization `owner`, `admin`, `editor`, `viewer`, `billing`; apps add namespaced permissions, never roles; wow2's `operator` becomes `editor` | developer, C3 |
| 2026-11-01 | external logins are external accounts in every member (`IdentityExternalAccount`, `ExternalAccountService`); the forward-only set keeps `identity_user_logins` and `login_provider` as named, mapped explicitly | naming build |
| 2026-11-01 | a person's display name lives in `identity_users.display_name` (`005-display-name`, an expand step, not readable by products); the `name` claim takes it first and erasure scrubs it; `admin create` sets it | admin create |
| 2026-11-02 | a session ends by `Identity:Sessions:Expiration`: `Fixed` takes `Lifetime`, or `ExtendedLifetime` when the sign-in asks to stay signed in (`isPersistent`); `Sliding` takes `SlidingLifetime` after each use and offers no such choice; the cookie, every OIDC refresh chain (named by `refresh_token_expires_in`), each BFF session and a bearer refresh family end with it | developer |
| 2026-11-01 | `admin create` exits `0` created or present, `1` refused (a handle, an id, an email or a phone held by someone else, or an existing organization the person does not own), `2` failed (usage, an invalid value, the database); it marks the given phone and email confirmed and sets no password | admin create |
| 2026-11-02 | `Identity:SignUp:Mode` = `Invite` (default) or `Open` replaces `Identity:SignUp:IsEnabled`: by default no one signs up without an invitation link, and an admin adds everyone else; `Open` is the configured "manual sign-up"; Ocharo runs `Invite` | developer |
| 2026-11-02 | an invitation is a single-use link into one organization carrying a fixed role, made by its owner or admin (only an owner invites an owner), lasting `Identity:Invitations:Lifetime` (7 days); naming a phone or email binds it to that identifier; it signs a new person up by email or phone, or adds a signed-in person (`006-invitations`, `/api/organizations/{id}/invitations`) | developer |
| 2026-11-02 | the managers' member routes live at `/api/organizations/{id}/members`: list, re-role, remove; `admin` manages beside the owner; the last owner stays | developer |
| 2026-11-02 | open sign-up's "guest" meaning and its email-first form are not built yet: `Open` signs up as before, with no role | developer |
| 2026-11-02 | a lockout refuses new credential attempts only: it never ends sessions, refreshes or authorize; ending sessions takes a stamp rotation or a revocation; an account without a password answers a password attempt as an unknown one and counts nothing | bug sweep #1 |
| 2026-11-02 | phone codes: one per number per 30 s, one per address per 5 s, 10 wrong codes per hour per number across codes; an undelivered code is discarded; a new code expires the older ones | bug sweep #2, #16 |
| 2026-11-02 | the Module's `identity-credentials` rate-limit policy (30 per minute per address) covers every sign-in, sign-up, reset and phone route; Argon2 runs 4 hashes at once and answers 429 after 5 s queued | bug sweep #2, #3 |
| 2026-11-02 | end-session revokes only with a valid `id_token_hint` or a same-origin POST; `prompt=login` and `max_age` hold through a sealed `fresh_sign_in` marker; `auth_time` is the session's start | bug sweep #10, #11 |
| 2026-11-02 | a deployed BFF on the in-memory session cache or an unpersisted key ring logs a warning at boot; products register a shared `IDistributedCache` and a persisted key ring | bug sweep #5 |
| 2026-11-02 | `Identity:Methods:Password` and `Identity:Methods:Email` (`IsEnabled`, on by default) switch password and email: a route whose method is off answers `404` `SignInMethodOff`, and the declaration and hosted pages leave it out; Ocharo's instance is phone-only (both off) while hosted email waits (OQ-2) | developer, cutover #10 |
| 2026-11-02 | the console signs in through the hosted pages as every person does: on Ocharo's instance the phone and the bot's Telegram code; a TOTP or passkey step comes when others get console access (OQ-1) | developer |
| 2026-11-02 | instance roles sit in `identity_roles`, seeded on the first grant; `admin create --role <instance role>` gives one to the owner, idempotently; any instance role opens the console (policy `identity-console`, `GET /api/console/session`); role claims never enter app tokens | cutover #11 |
| 2026-11-02 | the host CLI also makes invitations (`admin invite --organization --role [--phone]`): any catalog role, `owner` included, recorded as made by `Guid.Empty`; exit `0` created, `1` no such organization, `2` failed; the last line carries the link (widens the 2026-11-01 "first admin only") | developer, cutover #12 |
| 2026-11-02 | the invite sign-up lives in the generic instance (OQ-4): the invitation opens the hosted sign-up page; the person gives the profile `Identity:SignUp:Profile` asks for, then one verify action opens the bot; the shared contact proves the phone and links the chat, the person joins, and the browser that started it, holding an HttpOnly cookie secret, is signed in by its poll; no code is typed | developer, OQ-4 |
| 2026-11-02 | sign-up profile fields are settings: `Identity:SignUp:Profile:{DisplayName,Picture}` each `Off`, `Optional` or `Required`, carried in the declaration; Ocharo sets both `Required`; any custom field waits for after G3 | developer |
| 2026-11-02 | a profile picture is JPEG, PNG or WebP judged by its leading bytes, at most 5 MiB, sides 256–4096 read before decoding, re-encoded to one square 512 WebP that keeps no metadata; SkiaSharp (MIT) decodes and encodes, ImageSharp's split licence ruled out; the file input accepts exactly those types so iPhones send JPEG for HEIC | developer, orchestrator analysis |
| 2026-11-02 | pictures live in `identity_profile_pictures` behind `IProfilePictureStore` (File Storage adapter later), served anonymously at `/api/account/pictures/{userId}?v={version}` and given to apps as the `picture` claim under `profile` | developer |
| 2026-11-02 | app tokens and userinfo carry the active organization's `org_name` and `org_slug` beside `org_id`, and the BFF session's organization carries `name` and `slug`, so a product projects the organization row from the token alone | Ocharo platform |
| 2026-11-02 | invitations are made in the console only: an owner or admin signs in and makes a link with its role; the `admin invite` verb is removed (reverses the same day's row), and the host CLI keeps `admin create` for the first owner | developer |
| 2026-11-03 | `admin create` makes only the instance admin, once per instance (idempotent for that person; refused while another admin exists); organizations are made in the console by the instance admin, who becomes their owner, a preset id keeping a product's key; the console opens to anyone holding an instance role or owning or administering an organization, organization creation to the instance `admin` only (revises 2026-11-01 "the host CLI creates the first admin only" and the 2026-11-02 instance roles row) | developer |
| 2026-11-03 | a number a person holds confirmed and linked to one Telegram account never moves to another on a contact share: the share waits as a pending change (`identity_telegram_link_changes`, one day) for an owner or admin of the person's organization to approve or deny; a recycled number cannot take over the account | developer, analysis |
| 2026-11-03 | an organization's owners and admins manage members equally: either invites with any role, promotes to or demotes from owner, removes anyone and resolves Telegram changes; an organization may have zero owners, so removing, demoting or deleting the last owner never blocks (revises the 2026-11-02 rows "only an owner invites an owner" and "the last owner stays") | developer |
| 2026-11-03 | the console reads instance roles from the store on every request (`InstanceRoleRequirement`), never from the cookie; any role change, instance or organization, grant or revoke, rotates the person's stamp and revokes their sign-in sessions, so the Identity cookie, every app's refresh chain and every BFF session end and they sign in again with the new roles | developer |
| 2026-11-03 | the console has its own shell (fe sdk `AppShell`, app bar with the person and Sign out, organizations with Members and Invitations, "Create organization" for the instance admin); `/sign-in` passes a signed-in person on to the return URL unless the app asks for a fresh sign-in (`prompt=login`, `max_age`) | developer |
| 2026-11-03 | trusted contacts (slice 1): another person's confirmed, bot-linked phone receives an account's codes with the full power of its own phone; an invitation names one (the inviter or another member with such a phone, no other consent); a sign-up through it creates the person with no phone, redeems the invitation and records the contact in one transaction; `identity_trusted_contacts` plus `trusted_contact_user_id` on invitations, sessions and the sign-in log (`010-trusted-contacts`); `Identity:TrustedContacts:{IsEnabled,MaxPerPerson}`, off and 3 by default | developer, `product/features/trusted-contacts.md` |
| 2026-11-03 | sign-in through a contact: by the account's own number the code goes to its own chat unless a contact is picked from `phone-sign-in/targets`; a contact's own number answers an account choice (their own, then each account trusting them) redeemed by a single-use OTP-store token; the session and the log row name the contact, `amr` stays `otp`; an account holding an instance role has no contact in slice 1 | developer |
| 2026-11-03 | a code to a trusted contact is keyed by the contact and bound to its target (`IOtpService.CreateBoundAsync`/`VerifyBoundAsync`: the binding enters the digest, the limits stay the subject's), so the contact's 30 s window and failure budget cover every account's codes and no code serves another account; a send from an account's number also needs the number's own window and budget | developer ("per number as today, and per contact too"), implementation |
| 2026-11-03 | an own-phone session went through no contact and was started by a `phone_code` or `phone_contact` sign-in, read from the sign-in log row tied to it; changing an own phone and revoking a contact need one, the notice's one-press button excepted; adding a first phone works from any session through the bot's `add-phone` link, which re-checks at completion and rotates no stamp | developer, implementation |
| 2026-11-03 | revoking a contact, or the contact leaving, ends every session started through them; a contact may not leave while the last contact of an account with no own phone; a notice with "Revoke trust" reaches an account's own chat only when it has its own phone and a sign-in went through one of its contacts | developer |
| 2026-11-03 | a sign-up through the invitation's contact asks no phone or email the invitation names (the contact's code is the proof), and a person with neither email nor phone meets `IsConfirmedEmailRequired` through the contact who signs it in | implementation, open for the developer |
| 2026-11-05 | one mechanism between services: client credentials for a job with no person, token exchange for a call made for one; a token names one resource URI as its only `aud` and `{service}.{action}` scopes, lives 5 minutes (10 at most), has no refresh token and no row; API keys stay for callers outside Identity | developer, `identity-cutover.md` #52 |
| 2026-11-05 | a client names its grants, resources and scopes in `Identity:Server:Clients`, each resource under the grant that reaches it; `Identity:Server:Resources` is the registry; a client authenticates with its secret by default or a key set under `private_key_jwt`, per client and unknown to calling code | developer, 2026-11-05; T8 C3, C4 |
| 2026-11-05 | token exchange takes only a person's own access token, issued to the caller, while its session lives; the result keeps the person, writes permissions for the resource's namespace and adds `act`; a token that already has `act` can't be exchanged again | developer; T8 C6 |

- proposed, not confirmed: one registration reading `Identity:Mode = Module | Oidc`, with a shared `IdentityMode` enum
  in Abstractions and options bound from configuration, overridable in code (Q2).

# Ocharo slice — shortlist and gaps

*Last updated: 2026-10-28*

> The least Identity must build so Ocharo's apps (Desk first) sign in through Ocharo's own instance, and every gap
> between that and today's code. Source: the instances analysis in the hub (`wow-two-ws/ideas/identity-instances-2026-10/`,
> § 4–7, Q60–Q68), checked against the code on 2026-10-28. Order set by the developer on 2026-10-27: shortlist, close
> the gaps, deploy with Ocharo, verify; the rest of P1 follows.

## Inputs and limits

- Read: `product/context.md`, `engineering/planning/backlog.md`, `engineering/architecture/{architecture,contract}.md`,
  the feature specs, T4/T6/T10 of the analysis batch, and the code under `engineering/codebase/` (Module, Server,
  Client, the service host, tests).
- Not read, because another change is in flight: `Program.cs`, the SQL migration files, `IdentityDbContext`, the
  entity classes. Schema claims rest on `architecture.md`, T10 and the Module docs.
- Ocharo side, read-only: Desk (`ocharo-platform`) auth, SSO and Telegram code and its `access` migrations.
  Wheelhouse is read only through its migrate-step spec and the descriptor convention.
- Marked `unverified` where a claim rests on a doc or inference, not on code read.

---

## 1 · Verdict

- **The pieces exist as services, not as a sign-in.** OIDC server, sessions, sign-in log, keys, ring, organizations
  with roles, the OTP engine, the Telegram message sender, TOTP and recovery codes are built; none is reachable by a
  phone-code route, a page or the Client.
- **Four large gaps carry the slice:** the Client module with session routes (I19), the Telegram link and webhook
  (I20), the hosted pages, and the console. Everything else is medium or small.
- **Smallest path to "Desk signs in on dev" (G1)** leaves out console pages, back-channel logout, the OpenID
  Foundation suite, meters beyond readiness and the ops notifier; all of them land before the prod cutover (G2).
- **Two host-level fixes come first:** the service registers none of the phone, OTP or two-factor slices, and
  `register` is mapped unconditionally, which an invite-only instance cannot keep.
- **Six schema additions** follow the soft-delete migration now in flight: `otp_codes`, `throttles`, `link_requests`,
  `bot_updates`, `audit_events` and `memberships.app_access`.
- **Gates outside Identity block the deploy, not the build:** a pipelines tag that accepts pinned images and `migrate`
  (latest local tag is `v0.11.0`), Wheelhouse's migrate step (spec only), a bound domain, off-host backups for
  the cutover, a bot per environment and a published bot privacy policy.

---

## 2 · Shortlist

- **G1** = Desk signs in on dev through the dev bot (hub M9). **G2** = prod cutover for every Ocharo app (hub M10).
- Order is the build sequence. Row 9 (Client) needs only the OIDC server, so it can start beside row 2.
- Size: S ≤ 2 days · M ≈ a week · L > a week of one agent (unverified estimates).

| # | Item | What Ocharo needs | Exists today | Gap | Size | Order |
|---|---|---|---|---|---|---|
| 1 | Phone sign-in routes and method switches | phone, then code, then a cookie session with `sid` and `amr` `otp`; one answer for known and unknown phones; no self sign-up, no password | `UserPhoneService.cs:61,73` creates and verifies the code (null for an unknown phone); `SignInService.cs` completes lockout, preconditions, the 2FA ticket and the session; `PhoneSliceTests.cs`. The host registers none of `AddPhoneNumbers`, `AddOtpService`, `AddUserTokens`, `AddTwoFactor` (`HostConfiguration.Extensions.cs`); `register` and `login` are mapped always (`UserAccountEndpointRouteBuilderExtensions.cs:50-51`) | `POST phone-sign-in/send` and `POST phone-sign-in` (`useCookies`, `clientId`, ticket continuation for operators); sign-in log method `phone_code`; per-instance switches that turn off `register` and password `login`; register the slices | M | 1 |
| 2 | I15 durable OTP and throttles | hashed, request-bound codes; the attempt count survives a new code; 30 s resend; limits per phone, IP and client; shared by replicas | `OtpService.cs` (rate limit, generate, store, fixed-time verify) over the `IOtpRepository` seam; only `MemoryOtpRepository.cs`; `OtpRecord.cs:13` holds the code plain; attempts sit on the record, so a new code starts at 0; `RateLimitWindow` default 5 s (`OtpOptions.cs:20`); core ships `Web/RateLimit` | `EfOtpRepository` over `otp_codes` and `throttles` (T10 #17–18); HMAC code hash; request binding; throttle counters; per-IP and per-client policies on the new routes; prune job | M | 2 |
| 3 | Test mode | fake channels, a code sink and personas, refused when `DEPLOY_ENVIRONMENT` is set, so CI and Desk's tests sign in without a bot (Q18) | nothing: `Testing/README.md` calls the package reserved, `Identity.Tests.Conformance.csproj` says empty, a search for persona, test-mode and code-sink code finds none | fake `IOtpDeliveryHandler` plus a code-sink read route; persona sign-in route; fake `ITelegramBotClient`; a boot guard; every later row's tests use it | M | 3 |
| 4 | Operator second factor (TOTP; passkey optional) | owner and platform operator pass a second factor after the bot code (Q18); recovery codes | `UserTwoFactorService` (authenticator key, enable, hashed recovery codes); `manage/2fa/*` and `login/two-factor` map once `AddTwoFactor` registers (`endpoints.md`, `two-factor.md`); `TotpService`; passkey service and routes exist (`passkeys.md`), Fido2 4.3.1 upgrade open (backlog, Groundwork); a user with the flag always gets a ticket (`SignInService.cs`, `CompleteFirstFactorAsync`) | register `AddUserTokens` and `AddTwoFactor`; enforce "admin role or owner needs `TwoFactorEnabled`" on the console and admin API; enrolment screens are row 10 | S | 4 |
| 5 | I14 phone pre-registration and `app_access` | an admin adds a member (name, phone, role, apps) before the first sign-in; `ocharo` is created with Desk's organization id | `OrganizationService.cs` (create with a preset id, add, re-role, remove, last-owner guard); `UserAccountService.CreateAsync` makes a user without a password; `SetPhoneNumberAsync`; role to permission map from `Identity:Organizations` (`OrganizationOptions.cs`). No `app_access` (`IdentityMembership.cs`), no invitation code (search finds test names only), no pending state | `memberships.app_access text[]`; a pre-registration service (user, unconfirmed phone, membership, apps in one transaction, unique phone claim). The `invitations` table and links wait (P2) | M | 5 |
| 6 | App access check (Q61) | sign-in to an app missing from the member's `app_access` is refused and no token is issued | authorize, token and userinfo re-check person, stamp and session only (`OidcServerEndpointRouteBuilderExtensions.cs:44,101,117`); `OidcClientOptions.cs` has no app key; `OidcClaimMapper.cs:143` keeps only the client's namespace; `app_access` is not a token claim (`contract.md`) | an app key on the client entry (or the client id); the check in authorize, token (refresh) and userinfo with `access_denied`; the owner rule (P1); an E2E case beside `OidcCodeFlowTests.cs` | S | 6 |
| 7 | I20 Telegram link and bot webhook | the first sign-in links the Telegram id through the bot; webhook dedupe; the code goes to the stored chat; Desk's rules: sender, chat and contact owner match, contact phone equals the pre-registered one | `TelegramOtpDeliveryHandler.cs` (numeric chat id); `Telegram.Bot` referenced by the Module csproj; formatter in `uz`, `ru`, `en`; `IdentityUserLogin.cs` holds provider and key; `two-factor.md` addresses the `Telegram` login. `telegram.md` leaves the link flow to the consumer. Prototype: Desk's `TelegramAuthEndpoints.cs`, `TelegramBotUpdatesHandler.cs`, `009-telegram-access` | webhook route (constant-time `secret_token`, 64 KiB cap, `update_id` dedupe); update handler (start token, contact share, confirm phone, Telegram login row, first code); `link_requests` and `bot_updates`; start-token route for the pages (P3); `ITelegramBotClient` from settings; local-run polling; unlink; token and webhook secret as secret settings | L | 7 |
| 8 | Host CLI | `admin create` (first owner, `ocharo`, instance admin role) and a one-time sign-in link for a lost Telegram or TOTP | `keys generate` (`SigningKeyCommand.cs`) and the `migrate` verb tree (`deployment.md`); no `admin` command (search finds none) | `admin create` and `admin sign-in-link` (single use, short life, audited, prints the link only); optional `telegram webhook set\|info`; inside the image; tests | M | 8 |
| 9 | I19 Client module and session routes | Desk is a confidential client: `/api/identity/{me,sign-in,callback,sign-out,backchannel-logout}`, a cookie session, `org_*` claims in the principal, refresh inside the 10 minute access lifetime | `AddOpenIdConnectAuthentication` only (`OidcServiceCollectionExtensions.cs`: code and PKCE, `MapInboundClaims = false`, `SaveTokens`, userinfo claims); the fe `createCookieStrategy` and `createRedirectStrategy` already default to these routes (fe sdk `auth/`); `OrganizationClaimsPrincipalExtensions` reads the claims; no routes, no refresh, no logout receiver, no post-logout handling | `MapIdentitySession` with the five routes; token refresh and failure to re-authorize; sign-out through end-session with `id_token_hint`; claims to principal; the receiver (row 13); options binding (P6); Client side of the conformance cases | L | 9 |
| 10 | Hosted pages | phone, code, "Open bot" link, operator TOTP challenge and enrolment, recovery-code view, account page with sessions; `uz`, `ru`, `en`; `returnUrl` back to authorize | nothing real: `fe-identity` and `apps/web` hold the Greetings sample (`GreetingWidget.vue`, `HostedGreetingPage.vue`, `ConsolePage.vue`); the redirect side exists (`SignInPath`, `returnUrl` in `OidcServerEndpointRouteBuilderExtensions.cs`); core secure headers apply through `UseApiDefaults` (`HostConfiguration.cs`) | five or six `./kit` screens and their routing; `frame-ancestors 'none'` and `Referrer-Policy` confirmed on the pages (Q19, unverified); no third-party assets | L | 10 |
| 11 | I21 admin subset and audit | add a member, set role and apps, remove, unlink Telegram, revoke a member's sessions, read the sign-in log; an audit row per action | services exist: `OrganizationService`, `UserSessionService.RevokeAllAsync`, `SignInLogService.ListForUserAsync` (per person only); self-service `manage/sessions` routes. No admin routes, no admin policy, no private-host check in code, no audit table | `/api/admin/*` group; policy (admin role, 2FA on, private site); `audit_events` writes; log query by time, client and result | M | 11 |
| 12 | Console pages (G2) | the owner manages staff without a developer: members, sessions, sign-in log | nothing: no `./admin` export, the console route is the sample | `./admin` screens on Wheelhouse's layout; `apps/web` composition; layout review follows (backlog) | L | 12 |
| 13 | I18 back-channel logout (G2) | removing a member signs every app out at once | `ISessionRevocationHandler.cs` runs after every revoke; `OidcSessionRevocationHandler.cs` revokes grants and tokens; `sid` rides both tokens; core outbox with Postgres skip-locked (be sdk `Messaging/Reliability/Ef/`). `RemoveMemberAsync` rotates the stamp only (`OrganizationService.cs:161`), so sessions end when next seen (`sessions.md`); no `backchannel_logout_uri` (`OidcClientOptions.cs`), no logout-token signer, no sender, no receiver | client property and discovery flag; logout token signed with the active key; outbox rows and retries; removal revokes the member's sessions first; the Client receiver and revoked-`sid` store | M | 13 |
| 14 | `amr` after a second factor (G2) | `pwd` stays beside `otp` and `mfa` (RFC 8176) | `SecondFactorMethods = [otp, mfa]` (`SignInService.cs:31,311`); the ticket carries no first-factor methods | carry the first factor's methods in the ticket | S | 14 |
| 15 | Test mode in CI and conformance (G2) | Basic OP, RP-initiated logout and back-channel logout pass before live (Q28) | `conformance.yml` runs `dotnet test --filter "FullyQualifiedName~Client"` against the image, but `Identity.Tests.Conformance` holds no tests, so it checks nothing; in-process E2E covers code flow, sessions, key rotation, vault keys (`tests/Identity.Tests.E2E/Oidc/`) | OIDF suite job with a pinned tag and expected-failure files; `Testing`-mode persona form (plain HTML); broker suites stay out (no brokers) | L | 15 |
| 16 | OpenTelemetry meters and readiness | the T6 meters for sign-in attempts, code sends, lockouts, refresh reuse, key expiry; `/health` readiness | readiness exists: `AddDatabaseReadinessCheck<IdentityDbContext>()` (`HostConfiguration.Extensions.cs:50`) and the descriptor's `verify`; core ships Observability (metrics, OTLP, tracing); the host sets `EnableOtlpExporters = false`; no Identity meter or activity source in the repo | five meters, traces on authorize, token and webhook; labels without phone, IP or user id | S | 16 |
| 17 | Ops notifier | a push for admin events, webhook errors, key expiry | nothing | second bot token and chat; event hooks on the audit rows | M | 17 |

Sizes: 5 L · 8 M · 4 S.

---

## 3 · Cut or defer candidates

Each could wait until after G1. "Instead" is what the slice does without it.

| Candidate | Instead | Risk of deferring |
|---|---|---|
| Invitations table and invite links (I14 links) | pre-registered member row (row 5) | links later add a table and an accept page; no change to published tables. Wow2's step 4 builds them |
| `identity_user_phones` (Q56) | keep `identity_users.phone_number` and its confirmed flag; the Telegram id sits in `identity_user_logins` | moving later is an expand-and-contract on a column Desk may read; Haven needs several phones before it moves |
| Passkeys and the Fido2 upgrade | TOTP and recovery codes for operators | TOTP is phishable; two operator accounts accept it until wow2's pages land |
| `amr` fix (row 14) | none needed: a phone code gives `otp`, no password exists | wow2's `pwd` step-up reads wrong; cheap beside row 1 |
| Back-channel logout (row 13) before G1 | the Client refreshes inside 10 minutes and the token endpoint refuses ended sessions | a removed member keeps an app cookie until the next refresh; must land before the second client and the cutover (Q67 keeps it in the slice) |
| Console pages (row 12) before G1 | admin API plus `admin create` | the owner cannot add staff alone; must land before the cutover |
| OIDF suite in CI (row 15) before G1 | in-process E2E covers the flows | protocol drift reaches Desk unseen; the gate stays before live |
| Reversible person-level disable | remove the membership, revoke sessions (row 11) | re-adding loses role and apps; acceptable at this headcount |
| Meters beyond the five, traces (row 16) | logs and the sign-in log | slow to see abuse; no pumping risk while the only channel is a free bot |
| Ops notifier (row 17) | read the audit table and sign-in log by hand | no push on a webhook error or a new admin; T6 wants it, Monitoring may replace it |
| Brokers (M12) and events (M13) for Desk | Desk reads the published `identity_users` in the shared database (decision 2026-10-28) and takes role and permissions from the token | Desk couples to the published columns (expand-and-contract protects it); no `MemberRemoved` event, so Desk keeps no member projection yet |
| Log in with Telegram, Gateway, SMS, email, social, I22 | already deferred by the hub | none for Ocharo |

---

## 4 · External gates

### Wheelhouse and pipelines

- **Pipelines tag:** a pinned image and `migrate` need generator `v0.12.0` (`deploy-descriptor.md`, generator
  rules); the pipelines repository's newest local tag is `v0.11.0`. Blocks Ocharo's `deploy.yml` from naming the
  Identity image and its `migrate:` item.
- **Migrate step and bridge:** a spec for the Wheelhouse chat (`ideas/wheelhouse-migrate-step-spec.md`, 2026-10-27),
  not built (per the spec's status line). Until it ships, the owner runs `migrate` before each deploy that adds a
  migration (Q57). Identity's item must come first in Ocharo's `migrate:` list.
- **Domain binding:** the issuer sits on Ocharo's own domain, never a baseline host; unverified whether it is bound
  (Q30).
- **Off-host backups with a restore proof and the post-restore step:** gate the prod cutover only (Q68); dev does not
  wait.
- **Identity image release:** Ocharo pins a tag, so Identity cuts a version-track release after the slice is green.

### Ocharo

- **One bot per environment** (dev and prod), each with its own token and webhook secret; Desk's bot is configured by
  hand today, so the webhook moves from Desk to Identity in the same step.
- **Privacy policy for the bot profile:** publication is pending (Desk's `telegram-sign-in.md`).
- **Residency check:** confirm the host's location against the cross-border rules before the first real phone number
  (T6 § 1).
- **Desk's migration:** drops `access.*` after Identity's set is current (notes below).

### Owner's hand steps

1. Create the bots with BotFather; set the profile and privacy URL.
2. Generate the signing, encryption and ring certificates with the image (`keys generate`, three per environment);
   paste each into the vault; keep no copy.
3. Generate one client secret per app; store it in the app's vault namespace; Identity keeps the hash.
4. Add the DNS record for the issuer host.
5. Run `admin create` on the host for the first owner, enrol TOTP, save recovery codes.
6. Set the webhook with its `secret_token` (CLI verb if row 8 includes it, else by hand).
7. Each staff member opens the bot and shares their own contact at first sign-in.

---

## 5 · Desk swap notes

For a later Ocharo chat. Nothing here is done; Desk was read, not changed.

**What Desk has today**

- Accounts in schema `access` (migrations `002-access`, `009-telegram-access`, `010-app-sso`): users with text ids,
  organizations (uuid), memberships with `owner`, `editor`, `viewer`, sessions (8 h), Telegram links, link requests,
  durable codes, SSO codes and grants.
- A cookie session issued by `AccessSessionIssuer.cs`; route-scoped membership lookup in `OrganizationAccessFilter.cs`
  through `AccessRepository.FindMembershipAsync`, where `CanEdit` is owner or editor and `CanApprove` is owner.
- Its own SSO authority (`DeskSsoEndpoints.cs`, `Ocharo.Sso` and its snapshots in Studio and Marketing) and delegated
  tickets (`IntegrationTickets.cs`) for MCP, validated against live membership.
- The Telegram link flow starts after a password sign-in; Identity's starts at first sign-in with no password.

**What changes**

- **Sign-in:** Desk becomes the OIDC client `desk` (row 9). `AccessEndpoints.cs` (`/api/auth/session|login|logout`),
  `AccessSessionIssuer.cs`, `UserManager<AccessUser>` and the ASP.NET Identity package go; the frontend's
  `LoginPage.vue`, Telegram parts of `AccountSettings.vue` and the auth calls in `WorkspaceApi.ts` become redirects to
  the instance's pages.
- **Claims to access:** read `org_id`, `org_role`, `org_permission` instead of the membership query. Map `sub` to the
  name-identifier claim in one place to keep `AccessClaims.UserId` call sites. `app_access` is not a claim; a refused
  app arrives as `access_denied`.
- **Permissions:** `CanEdit` and `CanApprove` become `desk:`-namespaced permissions (P1); the role map lives in the
  instance's `Identity:Organizations`.
- **Ids:** `created_by`, `saved_by`, `approved_by`, `revoked_by` are `text` with a foreign key to `access.users` in
  `003-preparation`, `004-design-documents`, `007-sourcing` and `008-events`; organization columns reference
  `access.organizations`. A Desk migration re-types them to `uuid` and points them at `identity.identity_users` and
  `identity.identity_organizations`, after `DependsOn("identity", …)` (unverified in the migrator), then drops
  `access.*`. Dev rows reset; prod holds no data (hub § 4).
- **Organization id:** `admin create` takes Desk's existing organization id, so Desk and Catalogue rows keep their key.
- **Sessions:** `access.sessions` and `IsSessionActiveAsync` go; `sid` replaces the Desk session id in antiforgery
  binding (`SessionAntiforgeryDataProvider.cs`) and in ticket issue (`IntegrationTickets.Issue`).
- **Telegram:** remove `Identity/Telegram/*` and `009-telegram-access`; the bot token and webhook secret move to the
  instance; Telegram allows one webhook per bot, so the owner re-points it. Members are invited again and re-link at
  first sign-in (Q66).
- **SSO broker:** `Sso:Enabled` defaults off and Brand, Catalogue, Studio and Marketing deploy without sign-in, so
  the broker retires in Desk's change. `Ocharo.Sso` and its copies retire per app (hub § 4).
- **MCP:** `DeskMcpAuthenticationHandler.cs` and `IntegrationTickets.cs` keep their own tickets until the management
  API ships (hub § 4); rewire their membership check to the token's claims.
- **Config names:** Desk's legacy `Identity:Telegram:*` and `Identity:Sso:*` keys clash with the Client's `Identity:`
  section; rename or remove them in the same change.
- **Pins and descriptor:** Desk pins be sdk `10.0.71-beta` (`Directory.Packages.props`) and imports the
  `Identity.Otp` and `Identity.Jwt` namespaces from it; the Identity packages are a separate family (check the overlap
  when moving the pin). Ocharo's `deploy.yml` gains the pinned `identity` service (`needs: [postgres]`, its keys, the
  bot secrets, a client entry per app) and `migrate:` with Identity first.
- **Not needed for the first cut:** brokers, events, a member projection; Brand and Catalogue keep their single
  organization binding and check `org_id`.

---

## 6 · Points

- [ ] P1 Q61 closure: permissions namespaced per app (`desk:edit`, `desk:approve`, `desk:publish`, `desk:connections`; a flat `can-edit` is dropped for a namespaced client), `owner` reaches every app, other roles need an `app_access` entry → yes
- [ ] P2 Pre-registration is a pending member (user row plus membership), with no `invitations` table and no invite links in this slice → yes
- [ ] P3 Telegram start token binds the claimed phone, not a user, so unknown numbers get the same answer; per-IP caps limit minting → yes
- [ ] P4 Phones stay on `identity_users.phone_number` with the confirmed flag; `identity_user_phones` (Q56) waits for Haven → defer
- [ ] P5 Operator second factor is TOTP plus recovery codes now; passkeys come with wow2's pages → TOTP
- [ ] P6 Registration shape (Q2): ship the OIDC half only, `Identity:Mode = Oidc` with `MapIdentitySession`; the Module mode follows → yes
- [ ] P7 G1 is Desk signing in on dev; console pages, back-channel logout, the OIDF suite, meters and the notifier land before G2, not before G1 (narrows Q67's timing) → yes
- [ ] P8 Offboarding in this slice is remove membership plus revoke sessions plus logout; reversible disable waits → yes

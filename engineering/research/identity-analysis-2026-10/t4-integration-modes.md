# T4 — Identity integration modes

*Last updated: 2026-10-26*

> Every way a product plugs into `wow-two-sdk-beta.identity`, the broker contract behind those ways, and the code each
> one puts on the product side: one session facade with two modes hides the form, and a Form 3 product pulls the
> client only, never the server.

- Aliases `be`, `fe` as in [T3](t3-code-and-consumers.md); OpenIddict facts from [T1](t1-integrations.md) (7.7.2).
- Modes `M0`–`M13` are this track's; phases P1–P3 follow the [spec](../../../product/product.md); `(U)` = unverified.

---

## Mode catalogue

| # | Mode | How it works | Forms | Consumers (T3) | Product-side code |
|---|---|---|---|---|---|
| M0 | self-contained auth | SDK auth primitives with no identity store: cookie + upstream OAuth, API keys, `initData`, room tokens | none | Wheelhouse, TranscriptForge keys, Arcade, Hijinx | primitives only: `AddCookieAuthentication`, `AddGitHubAuthentication`, `AddApiKeyAuthentication`, own validators |
| M1 | in-process module | product hosts the store and slices, signs in on its own pages, issues its own cookie | 1 | pilots ×10 (password + lockout + stamp) | `AddUserAccounts<TUser>()…`, one EF migration per schema addition, sign-in routes, kit pages |
| M2 | same-origin BFF | product API is a confidential OIDC client: code + PKCE, tokens kept server-side, `__Host-` cookie for the SPA | 2, 3 (1 under option A) | ForeverPin, Haven, pilots after launch, TranscriptForge hosted, Ocharo ×6 | one registration + session routes; fe `createRedirectStrategy` |
| M3 | server-rendered app | M2's module; the challenge answers 302 instead of 401 | 2, 3 | none today; Identity's own console | `ChallengeMode = Mvc`; no fe |
| M4 | pure SPA public client | browser holds access and refresh tokens; PKCE; no backend session | 2, 3 | none | forbidden for first-party products |
| M5 | mobile shell | public client; system browser + PKCE (RFC 8252); tokens in Keychain or Keystore; bearer to the product API | 2, 3 | Hijinx, Matinee (Capacitor), PoseCoach (Swift); none signs in yet | client registration, M8 on the API, native strategy |
| M6 | Telegram Mini App | `initData` posted to the product backend; validated in-process (Form 1) or exchanged at the instance (Forms 2/3); cookie issued | 1, 2, 3 | Arcade (validates per request today) | one session route; `initData` pickup in the fe |
| M7 | CLI device flow | RFC 8628: CLI shows a code, the person approves on a hosted page, CLI polls for tokens | 2, 3 | none (Wheelhouse CI uses API keys) | client registration, M8 on the API |
| M8 | API resource, JWT | API validates access tokens offline: `iss`, `aud`, signature via cached JWKS, lifetime | 2, 3 | File Storage, Events, Harvest; Ocharo Studio, Marketing, Blog boundaries; M5/M7 APIs | `AddJwtBearerAuthentication` with `MetadataAddress` (exists) + claim policies |
| M9 | API resource, introspection | API asks the instance about each token (RFC 7662), caches briefly | 2, 3 | none; Ocharo's 3 ticket-introspection handlers retire | not offered in P1 |
| M10 | service-to-service | caller obtains a `client_credentials` token per audience, cached until expiry | 2, 3 | Forms 2/3 brokers → management API; products → Events, File Storage; Desk MCP and integrations | `AddOAuth2ClientCredentials` on the HTTP client (exists) |
| M11 | token exchange | caller swaps a user token for one with a downstream audience and an `act` claim (RFC 8693) | 2, 3 | org switch, impersonation, user-scoped calls to File Storage, M6 | exchange helper on the token client |
| M12 | management via broker | product server calls broker interfaces: read members, invite, change role, revoke sessions | 1, 2, 3 | Desk (member names and lists); team admin in products later | inject brokers; check the actor's permission first |
| M13 | events back | in-process events (Form 1); back-channel logout + signed webhooks (Forms 2/3); all land on `IEventHandler<T>` | 1, 2, 3 | Ocharo apps (logout on member removal, P1); Desk projection | handlers; receiver endpoints mapped by the client module |

### Browser modes (M2–M4)

- M2 is the default: every product already serves its SPA from the API's origin, so a BFF adds no hop.
- M2 maps sign-in challenge, callback, me, RP-initiated sign-out and the back-channel receiver; the product writes none of them.
- M2 tokens stay in the server-side ticket; the SPA sees only the cookie and `/api/identity/me`.
- M2 CSRF: `SameSite=Lax` cookie plus `AddSpaAntiforgery` (exists; ForeverPin uses it).
- M2 revocation: back-channel logout writes the `sid` to a revoked list that cookie validation checks.
- M2 safety net: refresh when the access token expires; a refused refresh ends the session, so a lost logout still lands.
- M2 on several instances needs the revoked list in a shared cache (HybridCache with Redis L2, exists).
- M2 variant for Haven: several services read one cookie, so the BFF writes the instance's access token into it.
- M2 prototype: Ocharo's SSO client (407 lines) has challenge, callback, CSRF and return-URL checks; port the checks, not its in-memory sessions.
- M3 shares M2's module; only the challenge mode differs.
- M4 is forbidden: any XSS reads the tokens; the IETF browser-based-apps BCP ranks a BFF first (U: draft status).
- M4 brings nothing here: no product is a static SPA without a backend.
- M4 stays possible server-side as a public-client registration, which M5 and M7 need anyway.
- Silent renew in a hidden iframe (`prompt=none`) breaks under third-party-cookie blocking; M2 never needs it.

### Native and device modes (M5–M7)

- M5 uses bearer tokens: a Capacitor origin (`capacitor://localhost`) makes API cookies third-party (U: CapacitorHttp may bridge cookies).
- M5 returns through a claimed https link (universal or app link) in preference to a custom scheme.
- M5 product API then runs two schemes, cookie for web and bearer for the shell, behind one policy scheme.
- M5 PoseCoach is Swift: ASWebAuthenticationSession or AppAuth-iOS; nothing comes from the fe sdk.
- M6 Form 1: Arcade's `initData` validator moves into the module; the cookie is issued in-process.
- M6 Forms 2/3: RFC 8693 exchange (OpenIddict ≥ 7.0, T1) with `initData` as the subject token; the instance holds the bot token.
- M6 validation: HMAC-SHA256 keyed from the bot token plus an `auth_date` window; an Ed25519 third-party signature also exists (U: field and Bot API version).
- M6 needs no sign-in UI; a person with no Telegram link falls back to the hosted phone + bot-code page.
- M7 waits for a named CLI: OpenIddict ships device flow; T2 moves it to P3.

### API resource and service modes (M8–M11)

- M8 is the default for every API resource: offline JWKS checks survive an instance outage for the token lifetime (T2, Clerk lesson).
- M8 revocation lags by the access-token lifetime → access tokens live 5–10 minutes.
- M8 validation exists: `AddJwtBearerAuthentication` accepts RS/PS/ES via `MetadataAddress` (T3); audience per app and org policies are missing.
- M9 fits only opaque reference tokens; no P1 consumer needs per-request revocation.
- M9 would add `OpenIddict.Validation` + `SystemNetHttp` to the product; keep it optional.
- M10's handler exists (`Http/Auth/OAuth2ClientCredentials`); each app gets scopes such as `identity.members.read`.
- M10 replaces the interim API keys of Events and Harvest; the Events spec § API already names client credentials.
- M10 carries the app's authority, not the person's; the product checks the person's membership before calling.
- M11 lets a downstream service enforce the person itself; P2, shipped with org switching and impersonation.

### Management and events (M12–M13)

- M12 is the broker contract below: Form 1 binds it to the module, Forms 2/3 to the Refit client.
- M12 authorization: the app's token limits the organizations it may touch.
- M12 actor check: the product checks the actor's `org_permission`, then passes the actor for the audit trail.
- M13 back-channel logout: the instance POSTs a signed `logout_token` (`sub`, `sid`, `events`) to each app's registered URI.
- M13 sender is ours: OpenIddict 7.7.2 has no back-channel logout (T1); Identity builds it on the SDK EF outbox.
- M13 webhooks go out through `AddWebhooks` (Standard Webhooks issuer) and come in through `AddInboundWebhooks` + `RequireWebhookSignature`.
- M13 delivery is at least once; handlers deduplicate by event id.
- M13 `SignedIn` needs no webhook: the client module raises it locally on the OIDC callback.

### People references

| Approach | Product stores | Names and roles read from | Cost | Fits |
|---|---|---|---|---|
| subject id only | `sub` (uuid) in its own rows | `GetManyAsync(ids)` batch + short cache | a broker call per list; stale names while the instance is down | Brand, Catalogue, Blog, Marketing, pilots |
| JIT profile | a product table keyed by `sub`, upserted on `SignedIn` from claims | local join | stale until the next sign-in; misses invited members | ForeverPin (Stripe, guest merge), Haven |
| projection | a `members` table fed by webhooks + a reconcile job over `ListAsync` | local join, search, sort | sync code; eventual consistency; one more copy to erase | Desk (assignees, approvals) |

- `sub` is the only foreign key to a person in every approach.
- A projection is an SDK add-on (table, handler, reconcile job), never per-product code.
- `PersonDeleted` must reach every JIT profile and projection (T2 erasure fan-out).

---

## Contract

### Rules

- one broker per capability, named for it (`IMemberBroker`); implementations name the provider (`wow-two-ws/conventions/development/backend/dotnet/core/mla/constructs/behavior/broker.md`).
- Form 1 implementations: `IdentityCore{Capability}Broker` over the module's services.
- Forms 2/3 implementations: `IdentityApi{Capability}Broker` over the Refit `IIdentityManagementApi` (`wow-two-ws/conventions/development/backend/dotnet/core/mla/constructs/behavior/client.md`: domain-qualified, the service also has flow and protocol surfaces).
- contract types are our records; no OpenIddict, EF or `IdentityUser<TKey>` type crosses it.
- operations return `Result` / `Result<T>` with an `AppError` from an Identity catalogue: `member_not_found`, `last_owner`, `invitation_expired`, `not_permitted`, `unavailable`.
- every write takes an `ActorModel` (acting person or the app) for the audit trail.
- every organization-level call takes `organizationId` explicitly; nothing reads ambient context.
- degradation sits in the `IdentityApi*` brokers: batch reads may serve cached names; writes surface `unavailable`.
- one conformance suite runs every broker against the module and against the image (`wow-two-ws/conventions/development/capability-forms.md` § Contract).

```csharp
public interface IMemberBroker
{
    Task<Result<MemberModel>> GetAsync(Guid organizationId, Guid subjectId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<MemberModel>>> GetManyAsync(Guid organizationId, IReadOnlyCollection<Guid> subjectIds, CancellationToken cancellationToken);
    Task<Result> ChangeRoleAsync(Guid organizationId, Guid subjectId, string role, ActorModel actor, CancellationToken cancellationToken);
}
```

### Operations

| Broker | Operation | Purpose | Phase |
|---|---|---|---|
| `IMemberBroker` | `GetAsync` | one member: profile, role, apps, status | P1 |
| | `GetManyAsync` | batch by subject ids, for lists | P1 |
| | `ListAsync` | page by search, role, status | P1 |
| | `FindAsync` | resolve by phone, email or Telegram id | P1 |
| | `ChangeRoleAsync` | set the role; last-owner guard | P1 |
| | `SetAppAccessAsync` | grant or revoke one app for the member | P1 |
| | `SuspendAsync` / `ReinstateAsync` | block sign-in to the org's apps; suspending revokes sessions | P1 |
| | `RemoveAsync` | end the membership; revoke sessions; emit `MemberRemoved` | P1 |
| `IOrganizationBroker` | `GetAsync` | one organization | P1 |
| | `ListForSubjectAsync` | a person's organizations, for the switcher | P1 |
| | `CreateAsync` | new organization with an owner, for self-serve B2B products | P2 |
| | `RenameAsync` / `DeleteAsync` | lifecycle | P2 |
| `IInvitationBroker` | `InviteAsync` | add by phone or email with role and apps; return the link | P1 |
| | `ListAsync` | pending, accepted, expired | P1 |
| | `ResendAsync` | send the link again | P1 |
| | `RevokeAsync` | cancel before acceptance | P1 |
| `ISessionBroker` | `ListAsync` | a person's live sessions: device, app, last seen | P1 |
| | `RevokeAsync` | end one session; back-channel logout follows | P1 |
| | `RevokeAllAsync` | sign out everywhere, or from one app | P1 |
| `ISignInLogBroker` | `ListAsync` | attempts by organization, person, app, time and result | P1 |
| `IAppBroker` | `GetAsync` | this app's registration: redirect URIs, methods, theme | P2 |
| | `UpsertAsync` | register or update an app from its deploy descriptor (Wheelhouse) | P2 |

- Person-level disable stays console-only: one person serves every app of the instance (D3).

### Session surface

Identical in both modes, so the fe never knows the form.

| Route | Module mode | OIDC mode |
|---|---|---|
| `GET /api/identity/me` | user from the cookie | user from the cookie |
| `GET /api/identity/sign-in?returnUrl=` | 302 to the product's kit page | 302 to the instance's authorize endpoint |
| `GET /api/identity/callback` | — | code exchange, cookie, `SignedIn` |
| `POST /api/identity/sign-out` | clears the cookie | clears the cookie; returns the end-session URL |
| `POST /api/identity/organization` | re-issues the cookie for another organization | re-authorizes for it (exchange in P2) |
| `POST /api/identity/telegram-mini-app` | validates `initData` | exchanges `initData` (P2) |
| `POST /api/identity/backchannel-logout` | — | revokes the `sid` |
| `POST /api/identity/events` | — | verifies webhooks; republishes on the local bus |

- fe defaults already target these: `createCookieStrategy` uses `/api/identity/me` and `/api/identity/sign-out`.
- `createRedirectStrategy` defaults to `/api/identity/sign-in?returnUrl=`.
- be serves none of them: `MapUserAccountEndpoints` maps `/login`, `/logout`, `/manage/info`; ForeverPin and Wheelhouse wrote their own.

### Claims vocabulary

| Claim | Value |
|---|---|
| `sub` | person id (uuid), stable across forms |
| `sid` | session id, matched by back-channel logout |
| `name`, `email`, `phone_number`, `locale` | profile |
| `org_id` | the active organization |
| `org_role`, `org_permission` | `{orgId}:{value}`, today's format, so `GetOrganizationRole` keeps working |
| `amr` | methods used (`otp`, `pwd`, `hwk`), for step-up |

- Vocabulary diverges today: Form 1 emits `ClaimTypes.NameIdentifier` and siblings (`IdentityClaimOptions.cs:10-19`).
- The OIDC client keeps raw `sub` (`MapInboundClaims = false`, `Oidc/OidcServiceCollectionExtensions.cs`).
- Form 1 emits `org_role` for every membership (`OrganizationService.GetClaimsAsync`); T2 advises one active organization per token.

### Events

| Event (`IEvent`) | Raised when | Form 1 | Forms 2/3 |
|---|---|---|---|
| `MemberAdded` | invite accepted or admin add | in-process bus | webhook `member.added` |
| `MemberChanged` | role or app access changed | in-process bus | `member.updated` |
| `MemberRemoved` | membership ended | in-process bus | `member.removed` |
| `PersonChanged` | name, avatar or locale changed | in-process bus | `user.updated` |
| `PersonDeleted` | erasure | in-process bus | `user.deleted` |
| `SessionEnded` | sign-out, revoke, removal | in-process bus | back-channel logout, republished locally |
| `SignedIn` | a sign-in into this app | in-process bus | client module, on the callback |

- `IEvent` and `IEventHandler<TEvent>` already exist in `Messaging/`; product handlers are identical in both modes.

### Out of the contract

- interactive sign-in, sign-up and sign-out: OIDC redirects in Forms 2/3, module routes in Form 1; never a broker call.
- invitation acceptance, Telegram link, MFA, passkey ceremonies, password reset: hosted pages or kit flows.
- token validation, refresh, client-credentials acquisition, back-channel reception: handlers inside the client module.
- self-service account (profile, linked sign-ins, passkeys): the hosted account page; products link to it.
- credentials: no broker sets a person's password, phone or passkey.
- console-only admin: policies, app registry editing, impersonation, CSV export.

---

## Form switch by configuration

### What differs

| Concern | Form 1 | Forms 2/3 |
|---|---|---|
| credentials entered on | product origin | instance origin |
| cookie issued by | the module, in-process | the client module after the code exchange |
| sign-in UI | kit pages in the product | hosted pages |
| SSO across apps | none | yes |
| logout of other apps | not applicable | back-channel |
| user store | product database | instance database |
| management calls | in-process services | HTTP + client credentials |

- A product touches sign-in at four seams: session routes, claims, events and its sign-in page.
- With the first three identical, the form is a configuration value; only the sign-in page moves.

### Options

| Option | How | Cost | Gain |
|---|---|---|---|
| A · OIDC in Form 1 | Form 1 embeds the OIDC server (OpenIddict pass-through over the product's pages); the product is an OIDC client of itself | OpenIddict + 4 store tables, signing keys and rotation in every Form 1 product; two cookies; an extra redirect pair; Form 1 tests need the protocol | one code path; M5–M7 work in Form 1; conformance identical |
| B · session facade | one registration, `Identity:Mode = Module` or `Oidc`; both map the same routes, claims, brokers and events | two implementations behind the contract (capability forms requires them anyway); claim vocabulary must converge; the sign-in page moves on a switch | Form 1 stays light; a Form 3 product pulls no server code |
| C · always remote | no Form 1 sign-in; every product needs an instance | dev and tests need a running instance; Wheelhouse impossible | one path |

- Recommend **B**, with A as an opt-in Form 1 add-on (`AddOidcServer()`) for a product issuing tokens to its own shells.
- T2 backs B: Better Auth serves cookie sessions in-process and offers OIDC as a separate provider plugin.

```csharp
builder.Services.AddIdentityIntegration(builder.Configuration.GetSection("Identity"))
    .AddModule(module => module.UseEntityFrameworkStores<AppDbContext>().AddArgon2Passwords()); // Module mode only
app.MapIdentitySession(); // the session surface above
```

```txt
Identity:Mode                 Module | Oidc
Identity:Authority            instance URL (Oidc)
Identity:ClientId, ClientSecret   Oidc; the secret per the secrets convention
Identity:Audience             this API's resource id
Identity:Management:Scopes    M10 scopes the brokers request
```

### Preconditions for B

- product code never reads identity tables, even in Form 1; an architecture test enforces it.
- `sub` survives a move: id-preserving import (T2 ranks it P2, before Form 3).
- the Form 1 principal switches to the contract claim names; pilots sign in once more after that release.
- the module and the client raise the same `IEvent` records.
- a Form 1 database exports into an instance; pilot rows already match the SDK schema and hash (T3).

### Tension

- Strict "configuration only" keeps the module package referenced, unused, in every Form 3 product.
- Package-minimal: a Form 1 → 3 move deletes the `.AddModule(...)` line and its package; product logic stays untouched.

### Dev and test

- Module mode with test personas runs a Form 3 product locally without an instance (T2: test mode P1).
- Cost: the dev path differs from production; a CI smoke suite against the image covers the OIDC side.

---

## fe side

### Hosted pages vs embedded kit

- Forms 2/3 use hosted pages: credentials only on the identity origin, SSO, one place for MFA.
- Passkeys bind to the instance's RP ID; a kit on another registrable domain cannot use them (U: Related Origin Requests).
- Embedding a remote sign-in also needs credentialed CORS and teaches people to type credentials anywhere.
- Form 1 renders the kit inside the product: one origin, the product's theme.
- One kit: the hosted pages app is kit screens + layout + theme loader, so both forms show the same screens.
- Kit screens take a flow client with a base URL: the product's in Form 1, the instance's in the hosted app.
- Hosted pages take each app's theme by SDK name (capability forms § Theming), `theme-ocharo` for Ocharo.
- ForeverPin's one-tap Google: a provider hint on the authorize request can jump straight to Google (U: parameter name).

### Per product type

| Product type | Sign-in UI | fe strategy | fe sdk ships |
|---|---|---|---|
| Form 1 SPA | kit route `/sign-in` | `createCookieStrategy` with `signIn` per method | `auth` (exists) + `auth-kit` |
| Forms 2/3 SPA (BFF) | hosted pages | `createRedirectStrategy` (exists, no importer yet) | sign-out that follows the end-session URL; "Manage account" link in `accountMenu` |
| server-rendered | hosted pages | none | none |
| Capacitor shell | hosted pages in the system browser | native strategy: PKCE, app-link return, secure token storage | `auth/native` (P3) |
| Telegram Mini App | none; silent `initData` | `createCookieStrategy` whose `signIn` posts `initData` | `auth/telegram` (P2) |
| CLI | hosted device page | — | — |
| product member admin | product page → product backend → brokers | — | `identity-admin` tables |
| hosted pages and console (the service) | — | — | `auth-kit` + `identity-admin` + theme loader |

### `@wow-two-beta/fe` additions

| Subpath | Holds | Phase | Peer deps |
|---|---|---|---|
| `auth` | exists; add end-session follow on sign-out, organization switch, `amr` step-up prompt | P1 | none |
| `auth-kit` | phone + code, Telegram link step, password + meter, provider buttons, passkey (WebAuthn helper), MFA, account parts, flow client | P1 phone, code, link; P2 rest | `vue`; `@tanstack/vue-form` optional |
| `identity-admin` | members, invitations, sessions, sign-in log tables; models mirroring the contract | P1 console; P2 products | `@tanstack/vue-query` |
| `auth/telegram` | `initData` pickup, Mini App theme sync | P2 | none |
| `auth/native` | PKCE, system browser, app-link listener | P3 | `@capacitor/browser`, `@capacitor/app`, a secure-storage plugin |

- No subpath stores tokens in the browser; `createBearerStrategy` stays for legacy only (the vault retires).

---

## Package needs

- Today every product references the mono-lib `WoW2.Sdk.Beta.Be`, which carries all of the below.
- The per-mode cut pays off only once T5 splits Identity out.

### Proposed cut

| Package (working name) | Holds | Heavy third-party deps |
|---|---|---|
| `Identity.Contracts` | brokers, models, `IEvent` records, claim constants, error catalogue | none (SDK results + the `IEvent` marker) |
| `Auth` primitives | cookies, API keys, allowlist + default-deny, claim normalizer, guest, current user | shared framework only |
| `Identity.Validation` | JWT bearer via JWKS, audience per app, organization policies | `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.IdentityModel.*`; optional `OpenIddict.Validation` for M9 |
| `Identity.Client` | OIDC client, session routes, logout receiver, revoked-sid store, `IIdentityManagementApi` + `IdentityApi*Broker`, webhook receiver | `Microsoft.AspNetCore.Authentication.OpenIdConnect`, Refit, `Microsoft.Extensions.Http.Resilience`, `Microsoft.Extensions.Caching.Hybrid` |
| `Identity.Client.Projection` | members table, webhook handler, reconcile job | EF Core (the product's context) |
| `Identity.Module` | store, slices, EF schema + migrations, flow routes, `IdentityCore*Broker`, OTP + channels, passkeys, TOTP, Telegram link | EF Core + Relational, `Fido2.AspNet`, `Otp.NET`, `Konscious.Security.Cryptography.Argon2`, `Telegram.Bot`, `Google.Apis.Auth` |
| `Identity.Providers` | upstream social login | 17 `AspNet.Security.OAuth.*` + Microsoft Google, Facebook, MicrosoftAccount handlers |
| `Identity.Server` | OIDC server, keys + JWKS, client registry, back-channel sender, device flow, exchange, `initData` grant | `OpenIddict.AspNetCore`, `OpenIddict.EntityFrameworkCore` (7.7.2 needs EF Core ≥ 10.0.11, T1) |
| `Identity.Testing` | fake brokers, personas, code sink, fake instance for E2E | test frameworks |
| service only (this repo's `service/`) | host, hosted pages, console, management API, webhooks out, Dockerfile | — |

### By mode

| Mode | be packages | fe subpaths |
|---|---|---|
| M0 | Auth (+ Providers for GitHub) | `auth` |
| M1 | Contracts + Module + Auth (+ Providers) (+ Server under option A) | `auth`, `auth-kit` |
| M2, M3 | Contracts + Client + Validation + Auth | `auth` (M2) |
| M5, M7 | product API: Validation | `auth/native` (M5) |
| M6 | Client (Oidc mode) or Module (Form 1) | `auth/telegram` |
| M8 | Validation | — |
| M9 | Validation + `OpenIddict.Validation` | — |
| M10 | caller: SDK `Http` (exists); callee: Validation | — |
| M11 | Client | — |
| M12 | Contracts + Client (Oidc mode) or Module (Form 1) | `identity-admin` |
| M13 | Contracts + Client or Module (+ Projection) | — |

- A Form 3 BFF product takes Contracts + Client + Validation + Auth.
- It pulls none of OpenIddict, EF Core (unless the projection), Fido2, Otp.NET, Argon2, Telegram.Bot, the 17 OAuth packages or Google.Apis.Auth.
- `IEvent` lives in `Messaging/`; Contracts needs it from a small abstractions package (T5).
- Upstream providers are the widest fan-out: Wheelhouse needs GitHub only and would pull all 17.

---

## Consumer mapping

| Product | Modes | Form | Product-side changes | When |
|---|---|---|---|---|
| Wheelhouse | M0 | none (T3 "Form 1": primitives, no store) | none; P2 option: register product apps via `IAppBroker` from deploy descriptors | — |
| ForeverPin | M2, M13 `SignedIn` | 3 | GIS + `AddGoogleIdTokenAuthenticator` → client + `createRedirectStrategy`; keep guest; merge guest on `SignedIn`; `users` → JIT profile keyed by `sub` (uuid kept) | first Form 3 consumer |
| Haven | M2 variant, M8 | 3 | its auth service becomes the BFF and writes the instance's access token to the shared cookie; its other services validate through JWKS; settle the phone model first | after relaunch decisions |
| pilots ×10 | M1 → M2 | 1 → 3 | flip `Identity:Mode`; import rows (same hash, same ids); drop identity tables; sign-in page → redirect | at each launch |
| TranscriptForge | M0 keys, M2 | 3 | add the client at hosted launch; keys stay | hosted launch |
| Sift | M2 if multi-user | 3 | settle single- versus multi-user first | — |
| Hijinx | M0, later M5 | — | room bearer stays | P3 |
| Arcade | M0 → M6 | 1 → 3 | validator moves into the module; later exchanged at the instance | P2 |
| Matinee | M5 if accounts arrive | — | none now | P3 |
| PoseCoach | M5 native, M8; console M2 | 3 | ASWebAuthenticationSession + PKCE; API validates bearer | P3 |
| Ocharo Desk | M2, M12, M13, M10 | 2 | client of Ocharo Identity; retire its account tables, SSO broker and tickets; members UI over brokers; projection for assignees; MCP and integrations → M10 (user-delegated MCP deferred, T2) | P1 |
| Ocharo Brand, Catalogue | M2 | 2 | the SSO client → the Identity client; the frontend SSO client → `createRedirectStrategy`; SSO settings → `Identity:*` | P1 |
| Ocharo Studio, Marketing, Blog | M2, M8 | 2 | as above; integration handlers → M8 with a per-app audience; delete the copied SSO clients and their sync script | P1 |
| File Storage, Events, Harvest | M8, M10, later M11 | 2, 3 | interim API keys → client credentials; authorize on `org_id`, app and role | after Identity P1 |
| Identity console | M2 against its own instance | 2, 3 | operators sign in through hosted pages with a second factor (T2) | P1 |

---

## Recommendations

- Ship one session facade (option B): `Identity:Mode = Module | Oidc` with identical routes, claims, brokers and events.
- Keep the OIDC server out of Form 1 by default; offer it as an opt-in add-on for products issuing tokens to their own shells.
- Converge the claim vocabulary before any OIDC client ships: `sub`, `sid`, `org_id`, prefixed `org_role` / `org_permission`.
- Make BFF (M2) the only browser mode for Forms 2/3; forbid pure SPA public clients.
- Use hosted pages for Forms 2/3 and the kit for Form 1; build the hosted pages app from the kit.
- Split the contract into six capability brokers in a dependency-free contracts package, with `IdentityCore*` and `IdentityApi*` implementations under one conformance suite.
- Cut packages for T5 as Contracts · Auth · Validation · Client · Module · Providers · Server · Testing; a Form 3 product takes the first four.
- Build the `/api/identity/*` session routes into the be sdk; the fe defaults already target them.
- Use `sub` as the only person foreign key; JIT profiles for per-person data; the SDK projection only where people are queried relationally (Desk).
- Route every event through `IEvent` on the local bus in both modes; revoke by back-channel logout plus refresh-on-expiry.
- Validate tokens offline (M8) at every API resource; no introspection in P1; client credentials via the existing SDK handler; exchange in P2.
- Forbid product code from reading identity tables even in Form 1, enforced by an architecture test.
- Defer M5 and M7 to a named consumer (P3), M6 and M11 to P2; keep public-client registration possible on the server.

---

## Open

- Form 1 → 3 switch: strictly configuration (module package stays referenced, inert), or configuration plus deleting the module line and package? Recommend the latter.
- Management authorization: the product checks the actor and passes it (P1), or token exchange with the person's token so the instance checks? Recommend the product check now.
- Pure SPA public clients for first-party products: forbid? Recommend forbid.
- ForeverPin sign-in: redirect with a Google provider hint, or keep GIS one-tap by exchanging the Google ID token at the instance (M11)?
- Organization scope in tokens: one active organization with switching (T2), or every membership as Form 1 emits today? Recommend one active organization.

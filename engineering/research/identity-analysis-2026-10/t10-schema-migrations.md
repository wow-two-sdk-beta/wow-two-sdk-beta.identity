# T10 — Identity schema, feature switches and migrations

*Last updated: 2026-10-28*

> One `identity` PostgreSQL schema holds every table the infrastructure may use (29 in P1, 40 at full extent),
> created by one linear SQL migration set shipped in `Identity.Module`, whatever the configuration; features read
> and write only their own tables, so a Form 1 product's database can be taken over by an instance with no copy.
> The be sdk migrator needs named sets for that, and instances must never share one identity schema.

- Aliases `be`, `fe` as in [T3](t3-code-and-consumers.md); packages as in [T7](t7-library-architecture.md); `(U)` = unverified.
- Facts come from reads on 2026-10-26 of `be` `src/Identity/**`, `src/Data/Migrations/**`, `src/Messaging/**`, the
  identity research, the pilots' identity code, Desk's account migrations and tracks T1–T9; nothing was built or run.
- Another agent was editing `be` during the read (kernel split); line numbers may shift.

---

## Verdict

- **Pick A, create the whole schema always:** one linear `identity` set builds every table; configuration only switches reads, writes, jobs and seeds. Empty tables cost under 1 MB.
- Schema authority moves from EF to SQL: `Identity.Module` embeds `Migrations/NNN-name/{Apply,Rollback}.sql` per dialect; the be sdk bespoke migrator runs it as its own set; EF only maps (answers Q31).
- The migrator needs four changes: named sets per host, `CREATE SCHEMA`, a schema placeholder in SQL, and a startup level check with an owner marker.
- Zero-copy takeover works PostgreSQL → PostgreSQL when ids, column names, hashes, claim names, passkey RP ID and secret encryption already match; today no product qualifies (pilots are SQLite, Desk, Haven and ForeverPin keep their own tables).
- Several instances on one identity schema is risky: they merge user bases, client registries and key rings. Give each instance its own database (default) or its own schema; only replicas of one instance share.
- Ids and names: `uuid` keys made as Guid v7 in code, `TKey`/`TUser` generics dropped from stored entities; schema `identity`, table prefix `identity_` kept, so SQLite (no schemas) and today's 11 tables need no rename.
- Extension: products and other modules keep their data in side tables keyed by `sub` in their own schema, plus claims contributors; `attributes jsonb` on users and organizations is the narrow exception.
- Haven (Q33): phones move to `identity_user_phones`, several per person, one primary; Desk (Q32) casts its GUID-string ids to `uuid` once.

---

## 1 · Table inventory

All tables sit in schema `identity` (configurable, § 6) and keep the `identity_` prefix; the prefix is dropped below
for width. "Switch" names the `Identity:{Feature}` section from § 2. "Today" refers to `ApplyIdentitySchema`
(`Core/IdentitySchemaModelBuilderExtensions.cs`), which maps 11 tables with no foreign keys.

### Accounts, credentials, organizations

| # | Table | Purpose | Key columns (sketch) | Key | Package | Switch | Phase | Today |
|---|---|---|---|---|---|---|---|---|
| 1 | `users` | the person; its id is `sub` | `user_name`, `email` (+ normalized, unique), `email_confirmed`, `password_hash`, `security_stamp`, `concurrency_stamp`, `two_factor_enabled`, lockout trio, `created_at`, `updated_at`; adds `display_name`, `locale`, `time_zone`, `avatar_ref`, `status` + `status_until` (disable, ban), `kind` (`member` · `guest`), `password_changed_at`, `must_change_password`, `last_sign_in_at`, `attributes jsonb` | uuid v7 | Module | always | P1 (adds P1–P2) | yes; `phone_number` moves to #2 |
| 2 | `user_phones` | several numbers per person (Haven); the one place phones live (T6 § 1) | `user_id`, `e164`, `confirmed_at`, `is_primary`, `verified_by` (`otp` · `telegram_contact`), `created_at`; unique `e164` where confirmed | uuid | Module | `Phones` | P1 | no: one `PhoneNumber` column |
| 3 | `user_logins` | external sign-ins: Telegram user id, Google subject, GitHub | `provider` + `provider_key` (PK), `user_id`, `display_name`; adds `linked_at`, `last_used_at` | composite | Module; Providers run the handshake | `ExternalLogins` + provider sections | P1 (Telegram) | yes |
| 4 | `user_claims` | static claims an admin sets | `user_id`, `claim_type`, `claim_value` | bigint identity | Module | `UserClaims` | P1 | yes (int) |
| 5 | `roles` · `role_claims` · `user_roles` | instance-wide roles: operators, console | name + normalized; role claims; user ↔ role | uuid · bigint · composite | Module | `Roles` | P1 | yes, 3 tables |
| 6 | `user_tokens` | per-person secrets: TOTP key, recovery-code digests, preferred second factor | `user_id`, `provider`, `name`, `value`; the TOTP key gets envelope encryption (T3: plaintext today) | composite | Module | `TwoFactor` | P1 (operators, Q18) | yes |
| 7 | `passkeys` | WebAuthn credentials | `user_id`, `credential_id` unique, `public_key`, `user_handle`, `sign_count`, `aaguid`, `name`, `created_at`, `last_used_at`; adds `transports`, `backed_up` (Fido2 4.x) | uuid | Module | `Passkeys` | P1 operators · P2 all | yes |
| 8 | `password_history` | last N hashes for the reuse rule (Q20) | `user_id`, `password_hash`, `created_at` | uuid | Module | `Passwords:History`, off by default | P2 | no |
| 9 | `organizations` | tenants | `name`, `slug` unique, `created_at`; adds `status`, `attributes jsonb` | uuid v7 | Module | `Organizations` | P1 | yes |
| 10 | `memberships` | person ↔ organization, one role (D3) | `organization_id` + `user_id` (PK), `role`, `created_at`; adds `app_access text[]` (spec: per-app access), `disabled_at`, `invited_by` | composite | Module | `Organizations` | P1 | yes, no app access |
| 11 | `invitations` | admin-added members by phone or email (I14) | `organization_id`, `target_kind`, `target`, `role`, `token_hash`, `invited_by`, `expires_at`, `accepted_at`, `accepted_user_id`, `revoked_at` | uuid | Module | `Invitations` | P1 | no |
| 12 | `organization_domains` | verified email domains, join by domain | `organization_id`, `domain`, `token_hash`, `verified_at` | uuid | Module | `Organizations:Domains` | P3 | no |
| 13 | `organization_connections` | per-organization SSO: Entra, Workspace, SAML | `organization_id`, `kind`, `issuer`, `client_id`, `secret_ref`, `settings jsonb` | uuid | Module stores; Providers run it | `Organizations:Connections` | P3 | no |

### Sessions, devices, codes, logs

| # | Table | Purpose | Key columns (sketch) | Key | Package | Switch | Phase | Today |
|---|---|---|---|---|---|---|---|---|
| 14 | `sessions` | one row per signed-in browser or device; its id is `sid`; revoke one or all; back-channel logout | `user_id`, `organization_id` (active), `client_id`, `amr`, `security_stamp`, `created_at`, `last_seen_at`, `expires_at`, `revoked_at`, `revoke_reason`, `device_id`, `ip_masked`, `user_agent`, `actor_user_id` (impersonation, P3) | uuid v7 | Module | `Sessions` | P1 | no; Desk's session table is the prototype |
| 15 | `refresh_tokens` | Form 1 JWT refresh: rotation, family revoke | `user_id`, `family_id`, `token_hash`, `security_stamp`, `created_at`, `expires_at`, `consumed_at`, `revoked_at`; adds `session_id`, `client_id` | uuid | Module | `RefreshTokens`; OpenIddict owns refresh in Forms 2/3 (T1) | P1 | yes |
| 16 | `devices` | remembered device (skip MFA), new-device alert | `user_id`, `token_hash` (device cookie), `label`, `platform`, `first_seen_at`, `last_seen_at`, `trusted_until`, `revoked_at` | uuid | Module | `Devices` | P2 | no |
| 17 | `otp_codes` | durable codes shared by instances (I15) | `subject_hash`, `purpose`, `channel`, `code_hash` (HMAC), `request_binding` (ASVS 6.6.2), `attempts`, `created_at`, `expires_at`, `consumed_at`, `client_id` | uuid | Module | `Otp:Store = Database` | P1 | no: memory store, `OtpRecord.Code` plaintext; Haven and Desk wrote their own |
| 18 | `throttles` | counters that survive restarts: per phone, per account, attempts across regenerated codes (T6) | `key` PK (`{kind}:{subject_hash}`), `window_started_at`, `count`, `blocked_until` | text | Module | `Otp`, `Lockout` | P1 | no |
| 19 | `link_requests` | one-time tokens: Telegram `start` link, invitation acceptance in the bot | `token_hash` PK, `purpose`, `user_id` or `invitation_id`, `session_id`, `telegram_id`, `expires_at`, `consumed_at` | text (hash) | Module | `Telegram` | P1 | no; Desk's link-request table |
| 20 | `bot_updates` | webhook dedupe, so retries never resend a code (T6) | `update_id` PK, `received_at`; pruned | bigint | Module | `Telegram:Bot` | P1 | no |
| 21 | `sign_in_log` | every attempt (I16) | `occurred_at`, `user_id`, `subject_masked`, `method`, `client_id`, `organization_id`, `result`, `reason`, `ip_masked`, `country`, `device`, `session_id`, `trace_id`; monthly partitions plus a default partition; 180 days | bigint identity | Module | `SignInLog` | P1 | no: log lines only |
| 22 | `audit_events` | security and admin events, append-only (T6 `identity_audit`) | `seq`, `occurred_at`, `actor_id`, `action`, `subject_id`, `organization_id`, `client_id`, `result`, `reason`, `trace_id`; P2 `prev_hash`, `hash`; a trigger refuses `UPDATE` and `DELETE` | bigint identity | Module | `Audit` | P1 table · P2 chain | no |
| 23 | `api_keys` | management-API keys, personal access tokens | `owner_kind`, `owner_id`, `name`, `prefix`, `hash`, `scopes text[]`, `created_at`, `expires_at`, `last_used_at`, `revoked_at` | uuid | Module store over Auth's `IApiKeyRepository` | `ApiKeys:Store = Database` | P2 | partial: Wheelhouse and TranscriptForge own their stores |
| 24 | `consents` | ToS, privacy, marketing acceptance with version and time (T2) | `user_id`, `document`, `version`, `accepted_at`, `client_id`, `withdrawn_at` | uuid | Module | `Consent` | P2 | no |

### OIDC server, keys, delivery, bookkeeping

| # | Table | Purpose | Key columns (sketch) | Key | Package | Switch | Phase | Today |
|---|---|---|---|---|---|---|---|---|
| 25 | `oidc_applications` | client registry (OpenIddict) | `client_id`, `client_secret` (hashed), `client_type`, `consent_type`, `display_name`, `redirect_uris`, `post_logout_redirect_uris`, `permissions`, `requirements`, `settings`, `properties` (theme, methods, `backchannel_logout_uri`) | uuid | Server | `Server` | P1 | no |
| 26 | `oidc_authorizations` | grants per subject and client | `application_id`, `subject` (`sub` as text), `scopes`, `status`, `type`, `creation_date` | uuid | Server | `Server` | P1 | no |
| 27 | `oidc_scopes` | scopes and their resources | `name`, `resources`, `display_name` | uuid | Server | `Server`, seeded from config | P1 | no |
| 28 | `oidc_tokens` | codes, refresh and device codes; revocation | `application_id`, `authorization_id`, `subject`, `type`, `status`, `reference_id` (hashed), `payload`, `expiration_date`, `redemption_date` | uuid | Server | `Server`; prune job (T6) | P1 | no |
| 29 | `oidc_sessions` · `oidc_resources` | OpenIddict 8 entities (T1) | per OpenIddict 8 | uuid | Server | `Server` | at the 7 → 8 move | no |
| 30 | `signing_keys` | only if the key source moves into the database; T6 picks host files for P1 | `kid`, `alg`, `use`, `certificate` (envelope-encrypted), `not_before`, `not_after`, `retired_at` | text (`kid`) | Server | `Keys:Source = Database` | P2, optional | no |
| 31 | `data_protection_keys` | the instance's own ring, certificate-wrapped (Q26) | `id`, `friendly_name`, `xml` | int identity | Server, service host only | always in Forms 2/3 | P1 | no: file system |
| 32 | `outbox_messages` · `inbox_messages` | back-channel logout and event delivery with retries | per core `Messaging/Reliability/Ef` | per core | core Messaging, mapped into the schema | `Sessions:BackChannel`, `Events` | P1 | in core; the host authors the DDL |
| 33 | `webhook_subscriptions` · `webhook_deliveries` | outbound webhooks (`member.added` …) | per core `Messaging/Webhooks/Persistence` | per core | core Messaging | `Webhooks` | P2 | in core |
| 34 | `instance` | one row: who owns this schema now (§ 4) | `owner_kind` (`module` · `service`), `instance_id`, `issuer`, `claimed_at` | singleton | Module | always | P1 | no |
| 35 | `migration_history` | the set's journal | migrator DDL (`ordinal`, `version`, `checksum`, `applied_by`) | `ordinal` | core migrator | always | P1 | engine yes; per-set use new |

- Counts: 29 tables in P1, 38 with P2, 40 with P3; today's 11 all stay, with added columns.
- Rows 32–33 reuse core tables in the identity schema; a Form 1 product with its own outbox keeps both.

### Product side, not in the identity schema

| Data | Home | Package |
|---|---|---|
| per-person product data (Stripe, guest merge, preferences) | a table keyed by `sub` in the product's schema (T4 JIT profile) | product |
| member list for relational queries (Desk assignees) | `members` projection in the product's database (T4) | Client, opt-in |
| revoked `sid` list | cache; a table only once a product runs more than one replica (U) | Client |
| push targets (FCM, APNs) | Notifications, keyed by `sub` and device | Notifications |
| avatars | File Storage; `users.avatar_ref` holds the reference (T7 port rule) | File Storage |
| the product's own Data Protection ring | the product's schema | core |

### Deliberately not tables

- purpose tokens (email confirm, reset, two-factor ticket): stateless HMAC bound to the stamp (I2).
- guest sessions: a sealed cookie until P2, when a guest becomes a `users` row with `kind = guest` (same id on upgrade).
- Telegram links: a `user_logins` row (`Telegram`, user id) plus a confirmed `user_phones` row (T3 Desk mapping).
- foreign password hashes from imports: kept in `password_hash` with their algorithm marker, rehashed at sign-in (T2).
- organization roles: configuration (`OrganizationOptions.Roles`); runtime custom roles (P3) would add `organization_roles`.
- several emails per person: one stays on `users`; `user_emails` waits for a consumer that needs more.

---

## 2 · Feature switches

### Mechanism

- One configuration tree, `Identity:*`, identical in every form; each feature is a section with `Enabled`.
- Sections already exist for `Passkeys`, `Otp`, `Organizations`, `TwoFactor`, `EmailSignIn` (`SectionName` constants).
- The service registers every feature; configuration alone decides (Forms 2/3).
- The Module does the same: `.AddModule()` registers all slices, and `Identity:{Feature}:Enabled` turns each on.
- Code `.AddX(o => …)` stays for code-only options: callbacks, seams, contributors.
- This replaces the 2026-09-28 ruling "an absent slice is an unresolvable service" (`identity-slices.md`) for the Module.
- An absent section means off, as the module model's optional blocks are inert.
- Moving forms copies the `Identity` section from the product's settings to the instance's; nothing is renamed.

```txt
Identity:Mode                         Module | Oidc
Identity:Database:Schema              identity (one per instance, § 5)
Identity:Database:ConnectionStringName  defaults to the product's
Identity:Phones:Enabled               true
Identity:Passkeys:Enabled             true · ServerDomain = registrable domain (§ 5)
Identity:SignInLog:Enabled            true · RetentionDays = 180
Identity:Server:Enabled               Forms 2/3 only
```

### Rules

- Startup validates dependencies and fails closed with a named error.
  - `Invitations` needs `Organizations`; `EmailSignIn` and `Phones` need `Otp`.
  - `Telegram` needs `Otp`, `ExternalLogins`, `Phones`; `Sessions:BackChannel` needs `Server` and the outbox.
- T6's production guard stays: persona sign-in, an ephemeral key or no ring refuses to boot.
- Defaults: Form 1 turns on accounts only; the service turns on `Server`, `Sessions`, `SignInLog`, `Audit`.
- Readiness (`/health`) reports the schema level and the enabled features.

### A table whose feature is off

| Aspect | Behaviour |
|---|---|
| DDL | created and migrated anyway (§ 4, option A) |
| reads and writes | none; its routes are not mapped (404); its brokers return `identity.feature_disabled` |
| jobs | no prune, partition or rotation job runs for it |
| seeds | none; scopes, clients and roles seed idempotently at startup only when the feature is on |
| claims | contributes none (`org_*` disappear with `Organizations` off) |
| existing rows | kept; turning the feature on again revives them (old passkeys sign in again) |
| purge | only by an explicit, audited command naming the database (`purge passkeys --i-understand-this-is <db>`) |

- Turning a credential type off blocks sign-in with it at once, because nothing reads its table.

---

## 3 · Extensibility

| Pattern | Data lives in | Who migrates | Forms | Use for | Verdict |
|---|---|---|---|---|---|
| `TUser` subclass with extra columns (today's `AddUserAccounts<TUser>`) | identity tables | the product, inside the identity schema | 1 only | — | retire: two owners of one table; breaks takeover and Form 3 |
| side table keyed by `sub` | the extender's own schema | the extender | 1, 2, 3 | product data; other modules' data | **primary** |
| claims contributor (`IClaimsContributor`, Q18) | nowhere; computed at sign-in | — | 1 in-process; 2/3 a client callback (T2) | claims a token needs from product data | **primary for tokens** |
| `attributes jsonb` on `users` and `organizations` | identity tables | nobody (no DDL) | 1, 2, 3 | small attributes Identity itself serves: hosted account page, claims with no callback | narrow exception |
| `user_claims` rows | identity tables | nobody | 1, 2, 3 | admin-set flags | exists |
| events (`PersonChanged`, `PersonDeleted`) | the extender's tables | the extender | 1, 2, 3 | keeping side tables and projections current; erasure | pairs with side tables |

- ~~No foreign key from a product table to `identity_users`, even in Form 1~~ → reversed 2026-10-28 (migrations M5):
  inside one database a product may key into the published `identity_users` and `identity_organizations`, maps them
  read-only and writes through the module or the API (M7); Form 3 has no such table, so there `sub` stays a plain
  `uuid` column.
- Other domains (File Storage, Events, Harvest, Notifications) never add columns to identity tables; they key on `sub` and `org_id` (T7 chain rule).
- Attributes are declared per instance (`Identity:Attributes:{name}`: type, max length, who may edit, emit as claim).
- Writes validate against the declaration; a row holds at most about 4 KB; keys are global or `{app}.{key}`.
- Attributes reach tokens only when declared as claims, inside the one-active-organization size budget (Q8).
- `PersonDeleted` must reach every side table (T2 erasure fan-out); the extender owns that handler.

---

## 4 · Migration strategy

### Options

- **A · whole schema always:** one linear set creates and evolves every table; features use only theirs.
- **B · on demand:** each feature group has its own set, applied when the group is first enabled.
- **C · hybrid:** a core set always, plus groups on demand (OIDC server, delivery, enterprise).

| Criterion | A · whole schema | B · on demand | C · hybrid |
|---|---|---|---|
| upgrades across versions | one history, one schema level per release | one history per group; a late-enabled group replays on a newer core, safe only if groups never alter core tables (sessions, guests and phones do) | two to four histories |
| Form 1 database taken over by an instance | every table exists; the instance changes configuration | first instance boot runs DDL for missing groups; needs DDL rights at runtime | every instance runs the server group, so the instance path equals A |
| several products | the same shape everywhere; "level 14" means one thing | up to 2^groups shapes | a few shapes |
| EF migrations vs SQL migrator | either; EF mixes history into the product's `__EFMigrationsHistory` | EF needs one context per group; SQL needs named sets | as B |
| rollback | roll forward; nothing to un-create | turning a group off leaves its tables, which converges on A | as B |
| empty-table cost | 0 bytes heap and about 8 KB per empty index; under 1 MB for 40 tables (U, approximate) | none | small |
| turning a feature on | configuration plus restart | configuration, restart and a migration event | mixed |
| PostgreSQL in production | one set, one lock | several sets, one lock each | as B |
| tests | one Apply → Rollback → Apply chain | every group at every core level | fewer than B |

- **Pick A.** It is the only option where takeover, fleet upgrades and tests stay one-dimensional.
- B's one real gain, no unused tables, is worth under 1 MB per database.
- On-demand work survives where it is cheap: partitions, seeds and jobs start when a feature turns on.

### EF today vs a SQL set

| Problem with today's EF ownership | Evidence | A SQL set in Module |
|---|---|---|
| every SDK table addition needs one EF migration per consumer database | T3; pilots froze their identity migration as raw SQL | the package ships the migration; consumers author nothing |
| column casing follows the host | pilots: PascalCase on SQLite; PostgreSQL hosts: `UseSnakeCaseNamingConvention()` (`Data/PostgresPersistenceServiceCollectionExtensions.cs:72`) | names fixed in SQL and in a Module-owned `IdentityDbContext` |
| identity history mixes into the product's | pilots apply their identity context beside the product context | own `identity.identity_migration_history` |
| conventions | default is bespoke SQL; one strategy per schema (`migrations.md` § Strategy) | complies, per schema |

- Authoring: change the EF model, generate DDL per dialect, hand-edit (partitions, triggers, partial indexes), commit as `Apply.sql` + `Rollback.sql`.
- OpenIddict's four tables follow the same path from its EF model (T1, T6); an OpenIddict major ships as its own release.
- A parity test fails when the applied schema and the EF model disagree, per dialect.
- Expand, then contract (T6): a column dies one release after code stops reading it.

### Who runs migrations

| Form | Sets on the database | Runner | When | Owner marker |
|---|---|---|---|---|
| Form 1, PostgreSQL | product set (product schema) + `identity` set (Module's embedded SQL) | the product's migration action; startup runner in development | each deploy, before the new image serves | `module` |
| Form 1, SQLite (pilots, unit tests) | the same, SQLite dialect, Module-mapped tables only | as above; one applicant (no advisory lock) | as above | `module` |
| Forms 2/3 | `identity` set only | the instance image's `migrate` step | each deploy | `service` |
| takeover | the product drops the Module (Q3) and its set; the instance claims the schema with an audited `claim` command | instance from then on | once | `module` → `service` |
| tests | Unit: SQLite in-memory via the migrator; Migrations and E2E: PostgreSQL Testcontainers (`MigratorPostgresFixture`) | migrator harness | per run | — |

- Conventions require an explicit production action (`migrations.md` § Lifecycle); T6 and T8 assumed apply-on-boot.
- The Module refuses to migrate, and to write, while the marker says `service`; a rolled-back product deploy cannot touch a taken-over schema.

### Changes the be sdk migrator needs

| Change | Today | Why |
|---|---|---|
| named sets: several `AddDatabaseBespokeMigrations` per host, each with its source, schema, history table and lock id | one set per host: `IMigrationRunnerService` and options register unkeyed (`MigrationServiceCollectionExtensions.cs:38-55`) | the product and Identity run side by side |
| `CREATE SCHEMA IF NOT EXISTS` before the history table | `EnsureHistoryTableAsync` assumes the schema exists (`PostgresMigrationDialect.cs:51`) | the `identity` history lives in its own schema |
| `{{schema}}` placeholder in SQL, checksum taken before substitution | none | one schema per instance (§ 5), isolated test schemas |
| read-only level check at startup, plus the owner marker | orphaned history fails `apply` only (`MigrationOrphanException`) | an instance must refuse a database newer than its code without trying to migrate |

- `MigrationOptions` already carries `SchemaName`, `TableName` and `AdvisoryLockId`; the gap is registration, not storage.
- Existing databases adopt the set with the migrator's mark-as-applied baseline (`bespoke.md` § 2.7).

---

## 5 · Shared databases

### (a) A new instance takes over a Form 1 product's database

Zero copy holds when all of these are true:

| Condition | Why | Today |
|---|---|---|
| PostgreSQL, the `identity` schema and the `identity` set's own history | the instance reads its level and continues | no: no PostgreSQL product carries the SDK schema |
| column names fixed by the Module, not the host | the instance maps the same columns | no: pilots are PascalCase |
| `uuid` ids, unchanged | `sub` survives (T4 precondition) | pilots yes; Desk text |
| the same password hasher reads the stored hashes | no reset wave | yes: pilots match the SDK hash (T3) |
| contract claim names in stored rows (`user_claims`, `role_claims`) | Form 1 writes `ClaimTypes.*` today (`IdentityClaimOptions.cs:10`) | no: converge first (T4) |
| passkey RP ID set to the registrable domain (`product.com`), not the app host | WebAuthn binds credentials to the RP ID; `id.product.com` can serve `product.com` only | config, not yet set anywhere |
| TOTP keys encrypted under a key from the `Identity` secrets, not the product's ring | the product's ring stays with the product | no: plaintext |
| instance version ≥ the Module version that last migrated | an older instance must refuse a newer schema | needs the level check (§ 4) |
| the product loses its grants on `identity` after the move | "a consumer never reads its tables" becomes a database fact | — |

- Not carried: the product's Data Protection ring, Form 1 refresh tokens and cookies; everyone signs in once more (T4 already expects that).
- Kept: users, credentials, organizations, memberships, passkeys, logs, audit.
- The product's backup now holds identity data; T6's post-restore step applies to it.
- The instance needs a role on the product's database with rights on `identity` only; Wheelhouse support for a second role is unchecked (U).

| Consumer | Engine and tables | Path |
|---|---|---|
| pilots ×10 | SQLite, `identity_*` PascalCase, Guid as `TEXT`, instants as `INTEGER` | copy into an instance at launch; SQLite never takes over |
| Ocharo Desk | PostgreSQL, its own schema, ASP.NET Core Identity tables, text ids, PascalCase | one-time conversion (Q32 below) |
| Haven | PostgreSQL, own user and phone tables, own OTP store | conversion after the relaunch decisions |
| ForeverPin | PostgreSQL, own users table (uuid ids) with the Google subject | conversion into `users` + a Google `user_logins` row |
| future Form 1 products on PostgreSQL | born on the `identity` set | zero copy |

- Moving a PostgreSQL product from `public.identity_*` into the schema is `ALTER TABLE … SET SCHEMA identity` per table, metadata only.
- Desk (Q32): ids are GUID strings (the ASP.NET Core Identity default), so `id::uuid` casts after a format check on the live database (U).
- Desk's remaining steps: rename columns, map its Telegram links to logins and phones, retire its session, code and SSO tables.

### (b) Several instances, one per product, on one database

**Risky with one shared identity schema; do not.** The instances stop being separate:

- user bases merge: `normalized_email` and phone uniqueness span products, so sign-up in one venture reveals or collides with an account in another.
- the weakest configuration wins: a product without MFA signs in a person another product protects with MFA, through the same row.
- the client registry is shared: any instance's authorize endpoint serves every product's clients.
- a shared `data_protection_keys` table shares cookie and antiforgery keys across products.
- organizations, slugs and lockout counters mix; "sign out everywhere" reaches only one instance's clients.
- upgrades go in lockstep: the advisory lock serializes migrations, but an older instance then meets a newer schema and must refuse.
- separating them later means an `instance_id` on every table: the tenancy retrofit D2 and D3 avoid.

What to do instead:

| Need | Do |
|---|---|
| per-product isolation (default) | a database per instance, as Wheelhouse already gives each product |
| several instances on one server or database to save cost | one schema per instance: `Identity:Database:Schema = identity_haven` |
| scale one instance | replicas of the same image and configuration on one schema (T6 N-ready list) |
| shared sign-in across products | clients of one instance; that is Forms 2/3 |

- D2 ("one instance per owner") widens to one instance per isolation boundary: owner, product or residency (folds Q6).
- D3 holds inside each instance; one person may exist in several instances with different `sub`, by design.
- Capability forms § Ownership assumes one owner per database; with takeover and per-schema instances, ownership becomes per schema.
- Concurrency stays safe: each set takes its own advisory lock, so product and identity migrations never block each other.

---

## 6 · Ids and naming

### Key type

| Option | Fits | Cost |
|---|---|---|
| `uuid`, Guid v7 made in code | T4's `sub`; ForeverPin and Haven ids; organizations and refresh tokens already use `Guid.CreateVersion7` | Desk casts once |
| text | Desk as is; foreign ids | wider indexes, case and format drift, `sub` format undefined |
| generic `TKey` (today) | any consumer choice | a SQL set cannot be generic; no consumer uses a non-Guid key |

- **Pick `uuid`;** drop `TKey` and `TUser` from stored entities (Open 4, 5).
- Users get EF's client-side generator today, passkeys `Guid.NewGuid()` (`UserPasskeyService.cs:81`); make v7 explicit with `TimeProvider`.
- v7 embeds the creation instant, so `sub` tells every client the sign-up time; acceptable while no pairwise `sub` is planned.
- Internal append-only rows (claims, logs, audit) take `bigint` identity for order and partitioning.
- OpenIddict's `subject` columns are text by design; they hold the `uuid` string.

### Schema vs prefix

| Option | PostgreSQL name | SQLite name | Grants | Instance per schema | Rename cost |
|---|---|---|---|---|---|
| prefix only (today) | `public.identity_users` | `identity_users` | per table | needs a configurable prefix | none |
| schema, bare names | `identity.users` | collides with product `users` unless prefixed | one `GRANT … ON SCHEMA` | `Schema` setting | dual names in EF and SQL per dialect |
| **schema + prefix** | `identity.identity_users` | `identity_users` (EF's SQLite provider ignores schemas, U) | one per schema | `Schema` setting | none: 11 tables and 10 pilots keep their names |

- **Pick schema + prefix.** One name in both engines; isolation, grants and `pg_dump -n identity` from the schema.
- The stutter in PostgreSQL is cosmetic; bare names would split every mapping by dialect.

### Living beside product tables

- Product tables stay in `public` or the product's schema, never in `identity`; ~~no cross-schema foreign keys~~ →
  keys into the published tables are allowed (reversed 2026-10-28, migrations M5).
- The Module owns an `IdentityDbContext` with snake_case pinned in both dialects; the host's naming convention cannot reach it.
- Pilots already keep a separate identity context, so the switch is a type swap there.
- `search_path` stays untouched; the context qualifies the schema.
- Status-like columns use native enums in the `identity` schema on PostgreSQL, text on SQLite (Postgres convention).
- One connection string by default (`Identity:Database:ConnectionStringName`); a separate role is optional in Form 1 and required after takeover.

---

## Open

1. Migration strategy: A whole schema always · B on demand · C hybrid → A.
2. Schema authority (Q31): SQL set in `Identity.Module` run by the be sdk migrator, EF as mapper · EF migrations per consumer → SQL set.
3. Migrator: add named sets, `CREATE SCHEMA`, `{{schema}}`, startup level check and owner marker to the be sdk before I14–I16 → yes.
4. Keys: `uuid` only, Guid v7 in code, `TKey` dropped from stored entities → yes.
5. Extension: side tables by `sub` + claims contributors primary, `attributes jsonb` narrow, `TUser` columns retired → yes.
6. Switches: configuration-only in both forms (Module registers every slice) · code `.AddX()` decides in Form 1 → configuration in both.
7. Naming: schema `identity` + `identity_` prefix · bare names · prefix only → schema + prefix.
8. Shared database (b): never one identity schema for two instances; database per instance by default, schema per instance allowed → yes.
9. D2: one instance per isolation boundary (owner, product or residency); capability forms § Ownership becomes per schema → yes (folds Q6).
10. Ocharo (Q32): own database with a one-time copy of Desk's converted account tables · take over Desk's database → own database, as the instance serves six apps.
11. Haven (Q33): `identity_user_phones`, several per person, one primary; `users.phone_number` retires → yes.
12. Production migration timing: an explicit `migrate` deploy step · apply on boot (T6, T8) → explicit step, boot in development only.
13. SQLite: a supported Form 1 dialect for Module-mapped tables, generated from the model · test-only → supported, pilots copy to PostgreSQL at launch.

---

## Sources

- `be` `src/Identity/Core/IdentitySchemaModelBuilderExtensions.cs` and the 11 entity files beside it; `core.md`.
- `be` options: `Core/**/…Options.cs`, `Otp/OtpOptions.cs`, `ApiKeys/ApiKeyOptions.cs`, `Guest/GuestSessionOptions.cs`, `Mfa/Totp/TotpOptions.cs` (34 files).
- `be` `Core/TwoFactor/UserTwoFactorService.cs` (TOTP key and recovery codes in stored tokens), `Otp/OtpRecord.cs`, `Otp/IOtpRepository.cs`, `ApiKeys/IApiKeyRepository.cs`.
- `be` `src/Data/Migrations/migrations.md`, `Bespoke/bespoke.md`, `MigrationOptions.cs`, `MigrationServiceCollectionExtensions.cs`, `PostgresMigrationDialect.cs`; `src/Testing.Data/Testing.Data.md`.
- `be` `src/Messaging/Reliability/Ef/` (outbox, inbox), `src/Messaging/Webhooks/Persistence/WebhookSchemaModelBuilderExtensions.cs`, `src/Tenancy/Tenancy.md`.
- `engineering/research/identity/identity-architecture.md`, `identity-slices.md` (from the be sdk) (rulings 2026-09-28 and 2026-10-21, I1–I22).
- Pilots: one pilot's identity context, migration and host wiring, and the other nine's migrations (SQLite, PascalCase; private repos).
- Desk: its account, Telegram and SSO migrations, its account entity and repository (private repo).
- Tracks: [T1](t1-integrations.md) § 1, § 6 · [T2](t2-benchmark.md) matrix and ranked misses · [T3](t3-code-and-consumers.md) § A, § C · [T4](t4-integration-modes.md) people references, claims, form switch · [T6](t6-hosting-security.md) § 2, § 7 · [T7](t7-library-architecture.md) § 3 · T8 § 2 (hub).
- [Identity spec](../../../product/product.md); `wow-two-ws/conventions/development/capability-forms.md` § Ownership; `wow-two-ws/docs/platform-apps.md` 2026-10-21 decisions.
- Conventions (`wow-two-ws/conventions/`): `development/backend/dotnet/core/mla/domains/persistence/` — `persistence.md` § Contract, `migrations/migrations.md`, `migrations/ef/ef-migrations.md`, `migrations/sql/bespoke-migrations.md`, `database/postgres/postgres.md`, `stored-json.md`.

# decisions/

*Last updated: 2026-10-27*

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
| 2026-10-26 | Ocharo gets its own database with a one-time copy of Desk's converted accounts | Q55 |
| 2026-10-26 | several phones per person in one table, one primary | Q56 |
| 2026-10-26 | production migrations run as an explicit `migrate` deploy step; apply-on-boot in development only | Q57 |
| 2026-10-26 | breaking changes are allowed in the SDKs and products while the products have no users | developer |
| 2026-10-26 | management screens ship in `@wow-two-beta/fe-identity`'s `./admin` export; `apps/web` composes them into the console, and a central services console or a product's admin page imports the same export | developer |
| 2026-10-27 | PostgreSQL only: the Module's SQLite dialect retires, as SQLite is banned for products (replaces Q58) | developer |
| 2026-10-27 | the default image (`service/Identity.Api`) is the general instance any owner pulls and configures; an owner who needs more composes the packages in a host of its own | developer |
| 2026-10-27 | wow2's own instance gets its own host image on the published packages, in a platform repository, later; the default image keeps serving Ocharo meanwhile | developer |

- proposed, not confirmed: one registration reading `Identity:Mode = Module | Oidc`, with a shared `IdentityMode` enum
  in Abstractions and options bound from configuration, overridable in code (Q2).

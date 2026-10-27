# codebase/

The code lives in `identity.backend-services/` (.NET) and `identity.frontend-services/` (Vue).

## Backend

- `Identity.BackendServices.slnx` groups `Packages/`, `Services/` and `Tests/`.
- `packages/` holds the NuGet family `WoW2.Sdk.Beta.Be.Identity.*`, moved out of the be sdk with namespaces kept:
  `Abstractions` (the contract), `Auth` (store-less auth), `Module` (Form 1), `Client` (Forms 2/3), `Providers`
  (upstream sign-in), `Server` (the OIDC server: `OidcServer/`, `Keys/` and `DataProtection/`) and `Testing` (the test
  kit, reserved).
  Each folder under a
  package is a namespace below `WoW.Two.Sdk.Beta.Be.Identity`, with its lead doc beside the code
  (`packages/Module/Core/core.md`). Inventory: `engineering/architecture/package-registry.md`.
  `Directory.Build.props` makes them packable by path; the core pin in `Directory.Packages.props` is their floor.
- The Module owns its PostgreSQL schema: `packages/Module/Migrations/Postgres/NNN-name/{Apply,Rollback}.sql`, embedded
  and run as the be sdk migration set `identity` (`001-accounts`, `002-oidc`, `003-spine`); `IdentityDbContext`
  (`packages/Module/Core/`) only maps it, OpenIddict's four `identity_oidc_*` tables, the sessions, the sign-in log and
  the Data Protection keys included.
- `service/Identity.Api` hosts the module by `ProjectReference`: `AddApiDefaults()` / `UseApiDefaults()`,
  `AddUserAccounts` over the service's PostgreSQL database, claiming the schema as `service`, with password sign-in,
  lockout, stamps, sessions, the sign-in log, organizations, `AddOidcServer` and `AddDataProtectionRing`; the cookie
  session on that database ring; the account API at `/api/account` and the OIDC endpoints (`/connect/*`,
  `/.well-known/*`); `SystemController`; and the pages in `wwwroot/` (filled by the `BuildSpa` target;
  `-p:BuildSpa=false` skips it). A local boot applies the set; a deployed boot only verifies it;
  `dotnet Identity.Api.dll migrate` applies it as the deploy step and exits `0` applied or current, `1` refused, `2` a
  script failed. `dotnet Identity.Api.dll keys generate --role signing|encryption|data-protection` prints one PEM
  bundle and exits before any host boots.
- Tests: `Unit` (package logic, no database: keys, the ring's wrapping and boot check, masks, claim mapping, client
  registration among them), `Integration` (the module over PostgreSQL, a database per host, schema from the migrator:
  every slice, sessions and the sign-in log through the account API, client seeding, session revocation through
  OpenIddict's stores, the database ring), `E2E` (the host in process over a database of its own: wire, pages,
  `/health`, the descriptor's dev check, the OIDC code flow against the JWKS, sessions and `sid`, the key rotation
  spikes), `Conformance` (the broker suites, none until the contract has brokers), `Migrations` (the module's SQL:
  apply → rollback → apply, model parity, the registration, the spine tables' round trip). `Integration`, `E2E` and
  `Migrations` run on PostgreSQL in Testcontainers, one container per tier, so they need Docker; `Unit` needs none.
  Run them with `dotnet test Identity.BackendServices.slnx`.

## Frontend

A pnpm workspace: `packages/fe-identity` (`@wow-two-beta/fe-identity`, published) and `apps/web` (hosted pages and console,
private). `scripts/deploy.mjs` copies `apps/web/dist/` to `identity.backend-services/service/Identity.Api/wwwroot/`.

Rules: wow-two-ws/conventions/development/repo/structure/domain-structure.md

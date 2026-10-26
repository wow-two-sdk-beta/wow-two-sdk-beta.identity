# codebase/

The code lives in `identity.backend-services/` (.NET) and `identity.frontend-services/` (Vue).

## Backend

- `Identity.BackendServices.slnx` groups `Packages/`, `Services/` and `Tests/`.
- `packages/` holds the NuGet family `WoW2.Sdk.Beta.Be.Identity.*`, moved out of the be sdk with namespaces kept:
  `Abstractions` (the contract), `Auth` (store-less auth), `Module` (Form 1), `Client` (Forms 2/3), `Providers`
  (upstream sign-in), `Server` (the OIDC server, reserved) and `Testing` (the test kit, reserved). Each folder under a
  package is a namespace below `WoW.Two.Sdk.Beta.Be.Identity`, with its lead doc beside the code
  (`packages/Module/Core/core.md`). Inventory: `engineering/architecture/package-registry.md`.
  `Directory.Build.props` makes them packable by path; the core pin in `Directory.Packages.props` is their floor.
- `service/Identity.Api` hosts the module by `ProjectReference`: `AddApiDefaults()` / `UseApiDefaults()`,
  `AddUserAccounts` over its own SQLite context (`Accounts/`), `SystemController`, and the pages in `wwwroot/` (filled
  by the `BuildSpa` target; `-p:BuildSpa=false` skips it).
- Tests: `Unit` (package logic, no database), `Integration` (the module over SQLite), `E2E` (the host in process:
  wire, pages, `/health`, the descriptor's dev check), `Conformance` (the broker suites, none until the contract has
  brokers), `Migrations` (the module's SQL, none until it ships any). None needs Docker. Run them with
  `dotnet test Identity.BackendServices.slnx`.

## Frontend

A pnpm workspace: `packages/fe-identity` (`@wow-two-beta/fe-identity`, published) and `apps/web` (hosted pages and console,
private). `scripts/deploy.mjs` copies `apps/web/dist/` to `identity.backend-services/service/Identity.Api/wwwroot/`.

Rules: wow-two-ws/conventions/development/repo/structure/domain-structure.md

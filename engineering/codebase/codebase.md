# codebase/

The code lives in `identity.backend-services/` (.NET) and `identity.frontend-services/` (Vue).

## Backend

- `Identity.BackendServices.slnx` groups `Packages/`, `Services/` and `Tests/`.
- `packages/` holds the NuGet family `WoW2.Sdk.Beta.Be.Identity.*`: `Abstractions` (the contract), `Module` (Form 1,
  with `Migrations/NNN-name/{Apply,Rollback}.sql`), `Client` (Forms 2/3) and `Testing` (fake + conformance suite).
  `Directory.Build.props` makes them packable by path; the core pin in `Directory.Packages.props` is their floor.
- `service/Identity.Api` hosts the module by `ProjectReference`: `AddApiDefaults()` / `UseApiDefaults()`, the migrator
  over the module's SQL, `GreetingsController` (the HTTP mirror of the contract), `SystemController`, and the pages in
  `wwwroot/` (filled by the `BuildSpa` target; `-p:BuildSpa=false` skips it).
- Tests: `Unit` (package logic), `Integration` (the module over a throwaway SQLite file), `E2E` (the host in process:
  wire, pages, `/health`, the descriptor's dev check), `Conformance` (one suite against module, client and fake),
  `Migrations` (the module's SQL). None needs Docker. Run them with `dotnet test Identity.BackendServices.slnx`.

## Frontend

A pnpm workspace: `packages/fe-identity` (`@wow-two-beta/fe-identity`, published) and `apps/web` (hosted pages and console,
private). `scripts/deploy.mjs` copies `apps/web/dist/` to `identity.backend-services/service/Identity.Api/wwwroot/`.

Rules: wow-two-ws/conventions/development/repo/structure/domain-structure.md

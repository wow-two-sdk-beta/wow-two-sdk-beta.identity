# wow-two-sdk-beta.identity — Backend Development Guidelines

Defer to wow-two-ws/conventions/development/backend/ and the domain repo shape in
wow-two-ws/conventions/development/repo/structure/domain-structure.md. Repo deltas:

- `packages/*` are the NuGet family: packable, XML docs, warnings as errors, a `README.md` beside each csproj (all set
  by path in `Directory.Build.props`). `service/` and `tests/` are not packable.
- A new package is a `packages/{Layer}/WoW.Two.Sdk.Beta.Be.Identity.{Layer}.csproj`; its id drops `.Two` (`WoW2.…`), its
  namespaces root at `WoW.Two.Sdk.Beta.Be.Identity`, and its row joins `engineering/architecture/package-registry.md`.
- The contract (`Abstractions`) holds no DI registration, options binding or I/O; a new broker member gets a
  conformance case in `packages/Testing` that every implementation passes.
- Store: SQLite through `DatabaseSettings:ConnectionString` (`DB_CONNECTION` wins). Tables carry the `identity_` prefix,
  since a Form 1 consumer applies them beside its own. A new table is a new
  `packages/Module/Migrations/NNN-name/{Apply,Rollback}.sql` pair; never edit an applied one.
- `dotnet test Identity.BackendServices.slnx` runs every tier; the backend build needs Node and pnpm for the pages.

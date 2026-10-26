# wow-two-sdk-beta.identity

Our own identity: user store, OIDC provider, hosted sign-in and admin console, shipped as an SDK library, a self-hosted image or a provided service.

## Lazy loading

- Do NOT pre-read files at conversation start; open a file only when the task needs it.
- `.claude/rules/file-references.md` is a lookup table, not a reading list.
- Navigate code under `engineering/codebase/` with `tree`/`find`/`grep` — it is not listed in references.

## Structure

```
product/                 ← the capability (no code): product · context · features · flows · brand
engineering/             ← the HOW
├── architecture/        ← architecture · contract · package-registry · decisions/ · flows/
├── development/         ← this repo's dev guidelines (defer to wow-two-ws/conventions/)
├── planning/            ← backlog.md + version-track/v{X.Y}/ (newest = active; drives the image version only)
├── deployment/          ← deploy.yml · Dockerfile · docker-compose.yml · deployment.md
├── research/ · scripts/ ← analyses · release helpers publish-packages.yml calls
└── codebase/            ← THE CODE — only place code lives
    ├── identity.backend-services/    ← Identity.BackendServices.slnx: packages/{Abstractions,Auth,Module,Client,Providers,Server,Testing} · service/Identity.Api · tests/Identity.Tests.*
    └── identity.frontend-services/   ← pnpm workspace: packages/fe-identity (npm) · apps/web (hosted pages + console)
```

## Rules

- Shape: wow-two-ws/conventions/development/repo/structure/domain-structure.md; repo-specific deltas only in `engineering/development/`.
- The contract lives in `packages/Abstractions`; Module (Form 1) and Client (Forms 2/3) implement it, and
  `Identity.Tests.Conformance` runs each broker suite (in `packages/Testing`) against both. No broker exists yet: the
  moved code exposes services and store ports (`engineering/architecture/contract.md` § Status).
- A package references only what its layer row allows; the service takes the packages by `ProjectReference` and adds
  only hosting, the HTTP mirror of the contract and the hosted pages.
- Package versions come from tags (`be-v*` NuGet, `fe-v*` npm) in `publish-packages.yml`; never commit a version,
  never create a GitHub Release for a package tag. The image version comes from the version track (`v*`).
- Core is pinned (`BackendSdkVersion`, the exact `@wow-two-beta/fe`); a needed core change goes to core first, then the
  pin moves here. `core-latest.yml` tests the newest core nightly; a red run is a fix here, not a gate.
- Schema changes are `packages/Module/Migrations/NNN-name/{Apply,Rollback}.sql` pairs; never edit an applied one.
- The backend build runs the frontend build (`BuildSpa`), so it needs Node and pnpm; no test tier needs Docker.

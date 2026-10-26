# wow-two-sdk-beta.identity

Our own identity: user store, OIDC provider, hosted sign-in and admin console, shipped as an SDK library, a self-hosted image or a provided service.

## Artifacts

| Artifact | Registry | Line · tag | Install |
|---|---|---|---|
| `WoW2.Sdk.Beta.Be.Identity.Abstractions` — the contract | nuget.org | `10.y.z-beta` · `be-v*` | `dotnet add package WoW2.Sdk.Beta.Be.Identity.Abstractions --prerelease` |
| `WoW2.Sdk.Beta.Be.Identity.Module` — Form 1, in process | nuget.org | `10.y.z-beta` · `be-v*` | `dotnet add package WoW2.Sdk.Beta.Be.Identity.Module --prerelease` |
| `WoW2.Sdk.Beta.Be.Identity.Client` — Forms 2 and 3, over HTTP | nuget.org | `10.y.z-beta` · `be-v*` | `dotnet add package WoW2.Sdk.Beta.Be.Identity.Client --prerelease` |
| `WoW2.Sdk.Beta.Be.Identity.Testing` — fake and conformance suite | nuget.org | `10.y.z-beta` · `be-v*` | `dotnet add package WoW2.Sdk.Beta.Be.Identity.Testing --prerelease` |
| `@wow-two-beta/fe-identity` — contract mirror, widget, forward | npm | `0.0.z` · `fe-v*` | `pnpm add @wow-two-beta/fe-identity` |
| `identity` service image | GHCR | `X.Y.Z` · `v*` | `ghcr.io/wow-two-sdk-beta/wow-two-sdk-beta.identity/identity`, deployed by Wheelhouse |

A product pins the family with one `IdentitySdkVersion` property beside `BackendSdkVersion`.

## Forms

```csharp
// Form 1 — in process, over the product's own database (it applies the module's migrations)
services.AddIdentity().Configure(options => options.ConnectionString = "...");

// Forms 2 and 3 — over the running Identity service; the product's code does not change
services.AddIdentityClient(options => options.BaseAddress = new Uri("https://identity.example.com/"));
```

## Layout

```
product/                 ← the capability: spec, state, features, flows, brand (no code)
engineering/             ← the how: architecture (contract, package registry), planning, deployment, scripts
└── codebase/
    ├── identity.backend-services/    ← packages/ (the NuGet family) · service/Identity.Api · tests/
    └── identity.frontend-services/   ← packages/fe-identity (npm) · apps/web (hosted pages + console)
```

## Run it (dev)

```bash
# Service (the https launch profile sets LOCAL_SETTINGS=true, so an optional appsettings.Local.json applies)
cd engineering/codebase/identity.backend-services && dotnet run --project service/Identity.Api --launch-profile https

# Pages and console, proxied to the service
cd engineering/codebase/identity.frontend-services && pnpm install --frozen-lockfile && pnpm dev
```

## Test it

```bash
# Backend: unit, integration, E2E, conformance and migration tiers (SQLite, no Docker); the build also builds the
# frontend workspace, so Node and pnpm are required
cd engineering/codebase/identity.backend-services && dotnet test Identity.BackendServices.slnx

# Frontend: lint, tests (builds fe-identity first), production build, packed-consumer check
cd engineering/codebase/identity.frontend-services && pnpm lint && pnpm test && pnpm build && pnpm check:package

# Release scripts: the tag version logic
bash engineering/scripts/test-next-version.sh
```

## Pointers

- Capability spec: `product/product.md` · state and decisions: `product/context.md`
- Contract: `engineering/architecture/contract.md` · packages: `engineering/architecture/package-registry.md`
- Releases: `engineering/scripts/scripts.md` · deployment: `engineering/deployment/deployment.md`
- Plan: `engineering/planning/backlog.md` · active version: newest `engineering/planning/version-track/v{X.Y}/`
- Conventions: `wow-two-ws/conventions/development/repo/structure/domain-structure.md`

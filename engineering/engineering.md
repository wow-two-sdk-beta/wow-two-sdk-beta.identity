# engineering/

The HOW of wow-two-sdk-beta.identity: seven NuGet packages, one npm package and the `identity` service image.

| Folder | Holds |
|---|---|
| `architecture/` | [architecture](architecture/architecture.md) (forms map, package graph, service) · [contract](architecture/contract.md) · [package registry](architecture/package-registry.md) · [decisions](architecture/decisions/decisions.md) · [flows](architecture/flows/flows.md) |
| `codebase/` | the code: `identity.backend-services/` (.NET 10) and `identity.frontend-services/` (pnpm, Vue 3) |
| `development/` | [this repo's guidelines](development/development.md) |
| `deployment/` | [how the service ships](deployment/deployment.md) |
| `planning/` | [backlog](planning/backlog.md) and `version-track/` |
| `research/` | [analyses](research/research.md) |
| `scripts/` | [release helpers](scripts/scripts.md) |

Stack: .NET 10, ASP.NET Core, EF Core as mapper over SQL migrations, PostgreSQL only (tests in Testcontainers),
OpenIddict for the protocol; Vue 3 on the fe sdk for the package and the hosted pages.

Rules: wow-two-ws/conventions/development/repo/structure/domain-structure.md

# File References

> **Lookup table only.** Do NOT read files proactively — only when the current task requires them.
> **Tracks docs outside `engineering/codebase/`.** Source files are navigated via `tree`/`find`/`grep`, never listed here.

| Question about | Read |
|---|---|
| The capability — problem, forms, model, phases | `product/product.md` |
| Name and one-line description (read by consoles) | `product/product.json` |
| State, tasks, decisions | `product/context.md` |
| Feature list + state | `engineering/planning/backlog.md` § Features |
| One feature's spec | `product/features/{feature}.md` |
| User / product flows | `product/flows/flows.md` |
| Hosted pages' brand | `product/brand/brand.md` |
| Technical overview · stack · map | `engineering/engineering.md` |
| System design — forms map, package graph, service role | `engineering/architecture/architecture.md` |
| The broker contract, every form | `engineering/architecture/contract.md` |
| Packages, layers, consumers | `engineering/architecture/package-registry.md` |
| Architecture decisions | `engineering/architecture/decisions/decisions.md` |
| What lives where in the code | `engineering/codebase/codebase.md` |
| Backend dev guidelines | `engineering/development/backend-guidelines.md` |
| Frontend dev guidelines | `engineering/development/frontend-guidelines.md` |
| Iteration / version workflow | `engineering/development/iteration-guide.md` |
| Backlog — every unbuilt item | `engineering/planning/backlog.md` |
| Active version | newest `engineering/planning/version-track/v{X.Y}/v{X.Y}.md` |
| Working rules for agents | `engineering/development/rules.md` |
| Deploy / ops | `engineering/deployment/deployment.md` |
| What deploys — service, sites, settings, dev check | `engineering/deployment/deploy.yml` |
| Releases — versions, tags, verification | `engineering/scripts/scripts.md` |
| Research and analysis batches | `engineering/research/research.md` |

## Source projects (`engineering/codebase/`)

> Individual files NOT listed — use `tree`/`find`/`grep`. Describe projects only.

### `codebase/identity.backend-services/`
| Project | What it is |
|---|---|
| `packages/Abstractions` | the contract: error catalogue, organization claims (brokers not built) |
| `packages/Auth` | store-less auth: JWT, cookies, API keys, allowlists, current user, guest sessions |
| `packages/Module` | Form 1: `AddUserAccounts` and its slices, OTP and its channels, account HTTP API |
| `packages/Client` | Forms 2/3: the broker over the service's HTTP API (empty until brokers exist) |
| `packages/Providers` | upstream sign-in: OAuth providers and claim normalization |
| `packages/Server` | the OIDC server (reserved) |
| `packages/Testing` | fakes and the conformance suites (empty until brokers exist) |
| `service/Identity.Api` | the deployable host: the module over its own context, the hosted pages |
| `tests/Identity.Tests.{Unit,Integration,E2E,Conformance,Migrations}` | the test tiers |

### `codebase/identity.frontend-services/`
| Project | What it is |
|---|---|
| `packages/fe-identity` | `@wow-two-beta/fe-identity`: `./contracts` · `./kit` · `./admin`; still the template's Greetings sample, unpublished |
| `apps/web` | hosted pages and the console, served by `Identity.Api`; still the template's sample |

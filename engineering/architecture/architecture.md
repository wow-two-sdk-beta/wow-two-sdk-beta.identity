# wow-two-sdk-beta.identity — Architecture

*Last updated: 2026-10-26*

One contract, two registrations, one service that hosts the first.

```
product Domain / Application ──► Abstractions (contract)
product composition root ──► Module (Form 1)  or  Client (Forms 2/3)
service/Identity.Api ──► Module (ProjectReference), + HTTP mirror of the contract + hosted pages (apps/web)
Client ──HTTP──► service/Identity.Api ──► Module ──► the service's store
```

| Form | Registration | Store owner |
|---|---|---|
| 1 · library | `services.AddIdentity()` + the module's migrations | the consumer |
| 2 · self-hosted | `services.AddIdentityClient(…)` + a `identity` container the consumer runs | the consumer's container |
| 3 · provided | `services.AddIdentityClient(…)` | the central instance |

| Project | Responsibility |
|---|---|
| `packages/Abstractions` | the contract — broker, model, constants, errors; no DI, options or I/O |
| `packages/Module` | Form 1 over the module's store; embedded SQL migrations |
| `packages/Client` | Forms 2/3 over the service's HTTP API |
| `packages/Testing` | the fake and the conformance suite |
| `service/Identity.Api` | hosting, the HTTP mirror of the contract, the hosted pages and console |
| `fe-identity` | the TypeScript contract mirror, the embeddable widget, the forward to the hosted page |
| `apps/web` | hosted pages (`/greet`) and console (`/`), on `fe-identity` through its exports |

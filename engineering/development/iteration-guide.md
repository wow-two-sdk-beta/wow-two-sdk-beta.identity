# wow-two-sdk-beta.identity — Iteration Guide

*Last updated: 2026-10-26*

> How a slice moves here: plan → implement → verify → consolidate → close. Repo-specific deltas only; the shared
> shape: wow-two-ws/conventions/planning/version-track/version-track.md.

- plan: take the next items from `planning/backlog.md` into the newest `planning/version-track/v{X.Y}/`.
- implement: contract first in `Abstractions`, then the Module and the Client behind it, then `fe-identity`.
- verify: `dotnet test Identity.BackendServices.slnx` (every tier, conformance included) and `pnpm lint && pnpm test
  && pnpm build && pnpm check:package` in the frontend workspace.
- verify a schema change with its `Apply` → `Rollback` → `Apply` chain in `Identity.Tests.Migrations`.
- consolidate: update `architecture/contract.md` and `package-registry.md` when the contract or a package changes.
- close: tick the version's tasks, set its status, move the settled facts into `product/`.
- releasing a package is a tag, not an iteration step: `engineering/scripts/scripts.md`.

# wow-two-sdk-beta.identity — Engineering Rules

*Last updated: 2026-10-26*

> Working rules for agents in this repo — repo-specific deltas only. Shared rules: wow-two-ws/conventions/.

- must treat every file here as public: no host, server or provider names, addresses, tailnet or internal URLs,
  secret names or values, and no private repos' paths or code; name a product and what it needs instead.
- must cite a hub document as `wow-two-ws/…` path text, never as a relative link.
- must keep vendor types out of `packages/Abstractions`; the contract references the kernel only.
- must add a table to the one `identity` migration set, never a set per feature; a feature that is off leaves its
  tables empty.
- must switch features by configuration (`Identity:{Feature}:Enabled`); code options carry only callbacks and seams.
- must not let a product read or write identity tables; products key their data on `sub`.
- must record a confirmed decision in `architecture/decisions/decisions.md` and `product/context.md` together.

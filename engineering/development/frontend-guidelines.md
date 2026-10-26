# wow-two-sdk-beta.identity — Frontend Development Guidelines

Defer to `wow-two-ws/conventions/development/frontend/`.

## Workspace

- Frontend root: `engineering/codebase/identity.frontend-services/`; Node `24.11.0` from `.nvmrc`, `pnpm@10.33.2`.
- `packages/fe-identity` is `@wow-two-beta/fe-identity`, published to npm: `./contracts` (the TypeScript mirror of the be
  contract) and `./kit` (`GreetingWidget`, `createGreetingsClient`, `forwardToHostedGreeting`). It peers
  `@wow-two-beta/fe` at `>=0.0.n <0.1.0`, `n` the pinned version; `check:package` enforces it.
- `apps/web` is `@identity/web`, private: the hosted pages (`/greet`) and the console (`/`). It takes `fe-identity` by
  `workspace:*`, through its `exports` only — the way a product would; the package never imports `apps/`.
- App source uses `bootstrap`, `presentation`, `application`, `domain`, and `integration` layers.
- Routes live in `src/bootstrap/router/Routes.ts` on the fe sdk router (`createAppRouter`, `lazyPage`); a new hosted
  page is one route there, its path a constant in `fe-identity/contracts`, its props decoded from the route.
- The kit's UI is form-agnostic: it calls the consumer's origin (Form 1) or the service's (`baseUrl`), never both.

## Commands

Run from the frontend root; `dev`, `typecheck` and `test` build `fe-identity` first, since `apps/web` reads its `dist/`.

```sh
pnpm install --frozen-lockfile
pnpm dev            # https on 8390, /api proxied to the service's https profile on 8388
pnpm lint && pnpm test && pnpm build && pnpm check:package
```

`pnpm dev:http` or `VITE_HTTPS=false pnpm dev` skips local certificate setup; `VITE_API_TARGET` picks another backend.

## Delivery

`pnpm build` builds `fe-identity`, then `apps/web/dist/`; `pnpm deploy` copies it into the service's `wwwroot/`, and a
backend build runs it too (`BuildSpa`). `publish-packages.yml` publishes `fe-identity` alone, at the version its
`fe-v*` tags and npm give.

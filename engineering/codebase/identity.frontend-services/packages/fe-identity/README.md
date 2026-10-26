# @wow-two-beta/fe-identity

The Identity domain's UI, in two modes:

- **Embed** — `GreetingWidget` greets through the Identity contract. It calls the consumer's own backend (Form 1, the
  default same-origin client) or the running Identity service (Forms 2 and 3, `createGreetingsClient({ baseUrl })`).
- **Forward** — `forwardToHostedGreeting` sends the user to the service's hosted greeting page, which links back to
  `returnUrl` when done.

```bash
pnpm add @wow-two-beta/fe-identity @wow-two-beta/fe vue
```

```ts
import { GreetingWidget, createGreetingsClient, forwardToHostedGreeting } from '@wow-two-beta/fe-identity/kit';
import { GreetingRouteConstants, type GreetingModel } from '@wow-two-beta/fe-identity/contracts';
```

The widget is styled with Tailwind utilities on the fe sdk theme; add the package to the consumer's Tailwind sources:

```css
@source '../../node_modules/@wow-two-beta/fe-identity/dist';
```

| Export | Holds |
|---|---|
| `./contracts` | `GreetingModel`, `GreetingConstants`, `GreetingRouteConstants` — the TypeScript mirror of the be contract |
| `./kit` | `GreetingWidget`, `createGreetingsClient`, `hostedGreetingUrl`, `forwardToHostedGreeting` |

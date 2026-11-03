# wow-two-sdk-beta.identity — Frontend Development Guidelines

Defer to `wow-two-ws/conventions/development/frontend/`.

## Workspace

- Frontend root: `engineering/codebase/identity.frontend-services/`; Node `24.11.0` from `.nvmrc`, `pnpm@10.33.2`.
- `packages/fe-identity` is `@wow-two-beta/fe-identity`, published to npm: `./contracts` (the TypeScript mirror of the
  account and organization APIs: shapes, `AccountRouteConstants`, `HostedPageRouteConstants`,
  `OrganizationRouteConstants`), `./kit` (`createAccountClient` and the components `PasswordSignIn`, `PhoneCodeSignIn`,
  `SignUpForm`, `InviteSignUpForm`, `ForgotPasswordForm`, `ResetPasswordForm`, `ChangePasswordForm`, and for account
  security `PhoneSettings`, `TrustedContactsList`, `TrustedByList`, `SessionList`) and `./admin` (`createOrganizationClient`
  and the embeddable `MembersPanel` and `InvitationsPanel` for an organization's owners and admins). It peers `@wow-two-beta/fe` at `>=0.0.n <0.1.0`, `n` the pinned version; `check:package` enforces it.
- `apps/web` is `@identity/web`, private: the hosted pages (`/sign-in`, `/sign-up`, `/confirm-email`, `/forgot-password`,
  `/reset-password`, `/change-password`, `/account/security`) in one card, and the console in its own shell (`/`,
  `/organizations/new`, `/organizations/:id/members`, `/organizations/:id/invitations`). It takes `fe-identity` by
  `workspace:*`, through its `exports` only — the way a product would; the package never imports `apps/`.
- App source uses `bootstrap`, `presentation`, `application`, `domain`, and `integration` layers.
- Routes live in `src/bootstrap/router/Routes.ts` on the fe sdk router (`createAppRouter`, `lazyPage`); a new hosted
  page is one route there, its path a constant in `fe-identity/contracts`, its props decoded from the route
  (`application/account/ReadAccountRequests.ts`).
- `HostedLayout` is the hosted pages' parent route: it renders the background, brand and one card once, and swaps
  the child pages inside the card through the fe sdk's `SwapTransition`; each page renders `HostedPage` with its own
  heading. The layout takes the fe sdk theme by registry id (`theme-{id}` from
  `@wow-two-beta/fe/themes.css`) and a background image (same-origin or https) from `SignInOptions`; `signInOptionsApi`
  reads the instance's methods and its one look (brand, theme, background) from `GET /api/account/sign-in-options?clientId=`,
  the clientId taken from the authorize `returnUrl`; it feeds the sign-in log and will narrow the methods, never the look.
- The kit's UI is form-agnostic: it calls the consumer's origin (Form 1) or the service's (`baseUrl`), never both.
- A link's one-time token stays out of the query: the change-password page reads it from the URL fragment and clears it.

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

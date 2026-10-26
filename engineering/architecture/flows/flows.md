# flows/

*Last updated: 2026-10-26*

> Identity's technical flows: the forward to a hosted page and back, a form switch, logout delivery. User flows:
> `product/flows/flows.md`.

## Forward to the hosted pages

1. The product's SPA calls `GET /api/identity/sign-in?returnUrl=` on its own backend.
2. In `Oidc` mode the backend answers 302 to the instance's authorize endpoint (code + PKCE, `state`, `nonce`).
3. The instance runs the sign-in on its hosted pages and redirects back with a code.
4. `GET /api/identity/callback` exchanges the code server-side, issues a `__Host-` cookie and raises `SignedIn`.
5. The SPA reads `GET /api/identity/me`; tokens never reach the browser.

- in `Module` mode step 2 answers 302 to the product's own sign-in route, and steps 3–4 run in process.
- `fe-identity` can forward a single flow (an invitation, a passkey, the account page) the same way and return to an
  absolute http(s) `returnUrl` only.

---

## Form switch

1. Copy the `Identity` configuration section from the product's settings to the instance's.
2. Move the product's people into the instance: a takeover of the product's `identity` schema, or a copy.
3. Remove the Module package and its registration; add the Client; set `Identity:Mode = Oidc`, the authority and the
   client credentials.
4. Everyone signs in once more; `sub`, organizations and memberships carry over.

- a takeover claims the schema with an audited command; from then on the Module refuses to migrate or write it.

---

## Logout delivery

1. A session ends: sign-out, revoke, suspension or removal.
2. The instance writes one back-channel logout per registered client into its outbox.
3. Each delivery POSTs a signed `logout_token` (`sub`, `sid`, `events`) to the client's registered URI, with retries.
4. The client revokes that `sid` and republishes `SessionEnded` on its local bus.

- a refresh refused at the instance also ends the app's session, so a lost logout still lands.

---

## Management call

1. A product backend checks the acting user's `org_permission`.
2. It calls a broker with the organization id and the actor.
3. Form 1: the Module runs it in process. Forms 2/3: the Client calls the management API with a client-credentials
   token scoped to that app.
4. The change raises its event; Forms 2/3 also send it as a signed webhook.

# Revocation signals

*Last updated: 2026-11-08*

> Identity tells the services holding its tokens when a session ended or a person's access changed, so a token verified
> offline stops counting before it expires. Asks and scope: the developer, 2026-11-05 (`identity-cutover.md` #52); this is
> I18 generalized. Build: v0.5 iteration 12. Tokens: [service tokens](service-tokens.md).

## Case

- A person signs out, or an owner removes them: every service holding their token should stop trusting it now.
- A person's role changes: they keep working with no sign-in, and their next token carries the new role.
- A signal can be lost, so a token's own short lifetime stays the limit: a lost signal costs minutes, never longer.

## Events

| Event | Fields | Meaning |
|---|---|---|
| `SessionEndedEvent` | `SessionId` (`sid`), `Subject` (`sub`), `At`, `TokensExpireBy`, `Issuer` | what the session issued stops counting |
| `AccessChangedEvent` | `Subject`, `OrganizationId` (`org_id`), `Kind`, `At`, `TokensExpireBy`, `Issuer` | `Changed`: joined or re-roled. `Removed`: left, or the organization went |

- A session ends on a sign-out, a revoke, a stamp rotation (password, email, phone, two-factor, an unlinked provider, a removed
  claim) and an account's deletion. Each signals at once; no later request or refresh is needed. A password change keeps the
  session it was made in.
- An account's erasure also raises `Removed` for each organization it belonged to. A soft delete raises only the session's end.
- They live in `Identity.Abstractions` as `IEvent`; a receiver is an `IEventHandler<T>`, so a broker can replace the delivery
  with no handler touched.
- `TokensExpireBy` is when the last token from before has expired; a receiver can forget the signal then.

## Delivery

```
session revoked / access changed → local event bus → OidcSignalForwarder → Messaging/Webhooks → POST signal URL
                                                                                 ↓
                 IEventHandler<T>: deny list · BFF · jobs ← local event bus ← receiver endpoint, verified through JWKS
```

- The bus runs it off the request, so a sign-out never waits on a slow service.
- `Messaging/Webhooks` carries each event as a signed POST, with retries on transient failures.
- Signature: scheme `key-signed`, no shared secret. `X-Webhook-Signature` is a JWT signed with Identity's token key, verified
  through its JWKS: `typ: webhook+jwt`, `iss`, `aud` (the signal URL, so a capture can't be replayed elsewhere), `iat`,
  `exp` (5 minutes), `jti` (the delivery id), `event`, and `body_sha256`, the hash of the body as sent.
- A body's `Issuer` must equal the signature's `iss`.
- Each target is delivered to at once on its own retries, so a dead or slow service holds up no other; an attempt takes at most
  `Signals:RequestTimeout` (5 seconds), and up to 8 events run at a time.
- A signal lost after its retries is logged and dropped: the token's lifetime is the limit.
- A receiver answers `401` for a bad signature, which isn't retried, and `503` while it can't fetch the issuer's keys or they
  don't list the signing key yet, which is. A forged key id reads the same way, and changes nothing.

## Registry

```jsonc
"Identity": {
  "Server": {
    "Resources": [ { "Name": "files", "Uri": "https://files.example.com/", "SignalUrl": "https://files.example.com/signals/identity" } ],
    "Clients":   [ { "ClientId": "ocharo-desk", "SignalUrl": "https://desk.example.com/api/identity/signal" } ],
    "Signals":   { "RequireHttps": true, "AllowPrivateNetworkTargets": false, "RequestTimeout": "00:00:05" }
  }
}
```

- Each client and resource names its own URL; none means it hears nothing.
- A signal URL needs a pinned `Issuer`, since the signature names it; the boot refuses otherwise.
- `AllowPrivateNetworkTargets` lets a signal reach a service on the instance's private network.

## Role changes

- A role change signs nobody out (`RoleChangeSignOut` is gone), and no stamp rotates: [role changes](../../engineering/codebase/identity.backend-services/packages/Module/Core/Roles/roles.md).
- `Changed`: a deny entry refuses tokens issued before `At`, the caller's cache drops the token on that `401`, and the
  next token has fresh claims. `iat` has whole seconds, so a token issued in the same second as `At` may take one more `401`.
- `Removed`: the same refusal ends that access; the session lives on for the person's other organizations.
- The Identity cookie reads roles from the store on each request.
- Instance roles never enter an app's token, so they raise no signal and end nothing.

## Receiving

```csharp
services.AddIdentitySignalReceiver(o =>
{
    o.Audience = "https://files.example.com/signals/identity";   // this service's signal URL
    o.TrustedIssuers.Add(new TrustedIssuer { Issuer = "https://id.example.com/" });
});
services.AddTokenDenyList();                                     // refuses matching bearer tokens
app.MapIdentitySignals("/signals/identity");
```

- A host that resolves issuers at runtime (`UseTrustedIssuerResolver`) hears their signals through the same resolver, so a tenant
  added without a restart is also revoked without one.

### Deny list

- Refuses a token whose `sid` matches an ended session; or whose `sub` and `org_id` match a change, when its `iat` is before `At`.
- An entry goes at `TokensExpireBy`; with none, after `DenyListOptions.DefaultEntryLifetime`.
- Keyed by issuer. Stored in `IDistributedCache`, so replicas sharing a cache share the list.
- A token with no `sid` is matched only by `sub` and `org_id`; exchanged tokens keep `sid`.

### BFF

- `AddIdentityBff` with `SignalUrl` set maps `POST {prefix}/signal`, verified against `Authority`.
- Session ended: the BFF session ends. Access changed: its next request refreshes first, so it carries fresh claims.
- The change is timed by the BFF's own clock when it hears the signal, so clock skew with the instance can't hide it.
- A refresh under way can't undo either: a session ended meanwhile stays ended, and a change heard meanwhile refreshes once more.
- A sign-in's claims date from before its code exchange, so a change heard during it refreshes too. An end heard during it finds no session yet; the first refresh, refused, ends it.
  A store shared across replicas narrows that race to the moment before the save; the signal's own gate is per process.
- A host that is both a BFF and a resource lists both URLs (`IdentitySignalReceiverOptions.Audiences`).

### Long operations

- `IPersonOperationTracker` (be sdk `Jobs`): each operation declares its reaction; sign-out continues and access loss cancels by default.
- A `sid`-tagged live operation cancels on sign-out. Accepted work runs as the service with the person recorded, and never
  stores their token.
- In-process: a job on another replica needs a broker to hear the signal.

## Live check

- For a named dangerous action, offline verification isn't enough: `policy.RequireLiveToken()` asks Identity through
  `POST /connect/introspect` (RFC 7662) on that request.
- Identity answers `active: false` once the session ended, or the person's membership or role in the token's organization differs.
- A client may introspect with `CanIntrospect` set; it authenticates as at the token endpoint.
- Apply it only to named actions; every other request stays offline.

## Limits

- SignalR stays out; it serves browsers later.
- The sender's retries span seconds and a receiver asks for new keys at most once per 5 minutes, so a key rotated inside that
  minimum, or during a longer JWKS outage, can still lose the signal; the token's lifetime bounds it.
- A signal is at-least-once, so handlers are idempotent; the receiver also drops a repeated delivery id.

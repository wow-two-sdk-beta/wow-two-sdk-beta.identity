# Identity.Signals

> The receiving side of Identity's revocation signals: a verified route, a token deny list, the bridge to long operations and
> the live check. The contract and the sender: `product/features/revocation-signals.md`.

```csharp
services.AddJwtBearerAuthentication(o => { /* … */ });
services.AddTokenDenyList();                                     // refuses matching bearer tokens
services.AddPersonOperations();                                  // optional: jobs hear sign-out and access loss
services.AddIdentitySignalReceiver(o =>
{
    o.Audience = "https://files.example.com/signals/identity";   // this service's own signal URL
    o.TrustedIssuers.Add(new TrustedIssuer { Issuer = "https://id.example.com/" });
});

app.MapIdentitySignals("/signals/identity");
```

## Receiver

| Check | On failure |
|---|---|
| `X-Webhook-Signature` is a JWT with `typ: webhook+jwt`, signed by the keys of a trusted issuer it names (asymmetric only) | `401` |
| `aud` is exactly a signal URL this host names, trailing slash included | `401` |
| `exp` and `nbf` hold on the host's `TimeProvider`, with 30 seconds of skew | `401` |
| `event` equals `X-Webhook-Event`; `body_sha256` equals the raw body's hash; `jti` is present | `401` |
| the body's `Issuer` equals `iss`; a body with none, or not a JSON object, fails | `401` |

- A bad signature answers `401`, which the sender doesn't retry. A signature that can't be checked yet answers `503`, which it
  does: the issuer's discovery is down, or its keys don't list the signing key yet (a rotation inside the 5-minute minimum between
  refreshes, or a refresh that failed). A pinned `SigningKeys` has nothing to wait for, so an unknown key there answers `401`.
- The delivery id is the signed `jti`, never the plain `X-Webhook-Id`, so a repeat can't dodge the drop by changing a header.
- `TrustedIssuer.Audiences` and `Partition` describe an issuer's tokens, and are ignored for a signal; the keys come from its
  `SigningKeys`, or its discovery through `BackchannelHttpHandler`.
- The route answers `401` and `409` itself rather than leave them to a host's global `AppException` mapping. `413` comes from core.
- A known event whose verified body can't be read answers `400`; an unknown event type answers `202` and is ignored.
- A verified signal goes onto `IEventBus` under its delivery id as message id, and handling runs off the request.
- A host that is also the issuer doesn't add this receiver: its local bus already carries the same events to the forwarder.

### The bus

`AddIdentitySignalReceiver` calls `AddInMemoryEventBus`, whose transport registrations all add only what is missing and whose
consumer is one hosted service, so it sits safely beside a host's own bus and repeats harmlessly. Its handler scan isn't
idempotent: a scanned assembly's handlers register again and run twice, so this package's handlers come in once, whichever of
`AddIdentitySignalReceiver` and `AddTokenDenyList` asks first.

- A broker registers **before** these calls. Its registration also adds only what is missing, so the first transport wins, and a
  broker registered afterwards is silently ignored. Handlers are `IEventHandler<T>` either way.
- Each handler takes what it feeds as optional: a host without `AddTokenDenyList` or `AddPersonOperations` still resolves it.

## Deny list

| Token | Refused when |
|---|---|
| any with a matching `iss` and `sid` | always, whatever its `iat` |
| any with a matching `iss`, `sub` and `org_id` | its `iat` is before the entry's `At`, or it has no `iat` |

- `Changed` and `Removed` both add an entry: either way a token from before holds the old answer.
- `iat` has whole seconds and `At` doesn't, so a token minted in the same second as the change counts as old: the caller's next
  token passes. The other way round an old token would pass until it expired.
- An entry goes at `TokensExpireBy`, else after `DenyListOptions.DefaultEntryLifetime` (1 hour). A signal already past its
  `TokensExpireBy` adds nothing.
- A repeated or late signal keeps the later `At` and the later end. Replicas writing one entry at once can lose the later `At`;
  the token's lifetime stays the limit.
- Stored in `IDistributedCache`, in memory unless one is registered, so replicas need a shared one. A bearer request reads up to
  two more keys. A cache that can't answer fails the request rather than let the token through.
- Refusal wraps `OnTokenValidated` on the default `JwtBearer` scheme, keeping a host's own handler, which runs only for a token
  that passes. It reads the token's own claims, so a host's claim mapping can't hide them. `JwtBearerOptions.EventsType` is refused
  at the scheme's first use, since the wrap would never run.
- No signal, no entry: a lost signal leaves the token passing until its own `exp`.

## Long operations

| Signal | Call on `IPersonOperationTracker` |
|---|---|
| `SessionEndedEvent` | `SessionEnded(sid, sub)` |
| `AccessChangedEvent`, `Removed` | `AccessLost(sub, orgId)` |
| `AccessChangedEvent`, `Changed` | none: a role change continues |

The tracker is optional and in-process: `person-operations.md` in the be sdk holds each operation's reaction.

## Live check

```csharp
services.AddLiveTokenCheck(o =>
{
    o.IntrospectionEndpoint = new Uri("https://id.example.com/connect/introspect");
    o.ClientId = "files";
    o.ClientSecret = secret;                                     // or o.ClientPrivateKey
});
app.MapPost("/files/purge", Purge).RequireAuthorization(policy => policy.RequireLiveToken());
```

Details: [authorization.md](../Authorization/authorization.md).

# Service tokens

*Last updated: 2026-11-05*

> One service calls another with a short token from Identity, bound to one audience and verified offline. Asks and scope:
> the developer, 2026-11-05 ("the best way ... so that it's future proof"); shapes from Files' `t8-identity-contracts.md`
> C1-C9, with the differences below. Build: v0.5 iteration 11.

## Case

- Editorial's image worker calls Desk, live public builds read product APIs, and every product server calls Files.
- A job with no person uses client credentials: the token names the app.
- A call made for a person uses token exchange: the token keeps the person and adds `act`, the app acting for them.
- API keys stay only for callers outside Identity.

## Registry

```jsonc
"Identity": {
  "Server": {
    "ServiceTokenLifetime": "00:05:00",     // at most 10 minutes; no refresh token
    "Resources": [
      {
        "Name": "files",                    // clients refer to this, the same in every environment
        "Uri": "https://files.example.com/", // the `resource` a caller sends and the only `aud` of its tokens
        "Scopes": [ "files.read", "files.write" ], // {service}.{action}
        "PermissionNamespace": "files"      // an exchanged token's org_permission; null keeps every permission
      }
    ],
    "Clients": [
      {
        "ClientId": "ocharo-editorial",
        "ClientSecret": "<from the vault>",      // or "JsonWebKeySet": "{ \"keys\": [ … ] }", public keys only
        "RedirectUris": [ "https://editorial.example.com/signin-oidc" ],
        "Grants": [ "authorization_code", "client_credentials", "token_exchange" ], // empty means the code flow alone
        "Resources": [ "desk" ],            // reached as itself
        "ExchangeResources": [ "files" ],   // reached for a person
        "Scopes": [ "desk.media", "files.read" ],
        "OrganizationId": null              // the org_id of its own tokens, for a client bound to one
      }
    ]
  }
}
```

- A resource URI is written as `Uri.AbsoluteUri`, so `https://files.example.com/` with its slash; the boot refuses another form.
- A scope belongs to the resources that list it; two instances of one service may share `files.read`.
- A client lists a resource under each grant that reaches it; a resource under `ExchangeResources` alone refuses client credentials.
- A client without `authorization_code` needs no redirect URI, and the boot refuses one.

## Tokens

| Claim | Client credentials | Token exchange |
|---|---|---|
| `iss` · `jti` · `exp` · `iat` | yes | yes |
| `aud` | the resource URI, only | the resource URI, only |
| `sub` | the client id | the person's id |
| `client_id` | the client id | the client id |
| `scope` | every scope the client may use on that resource | the same |
| `org_id` | only for a bound client | the subject token's active organization, while the person belongs to it |
| `org_name` · `org_slug` · `org_role` · `org_permission` | no | yes, written fresh, permissions kept to the resource's namespace |
| `sid` · `amr` | no | the subject token's |
| `act` | no | `{ "sub": "<client id>" }` |

- OpenIddict validates `resource` and checks the client's permission, but never turns `resource` into `aud`; `aud` comes from the registry.
- A request names exactly one `resource`; none, several, or one this client's grant doesn't reach answers `invalid_target` (RFC 8707).
- One token per person and target, never one per action (developer, 2026-11-05): a `scope` parameter is checked, not
  narrowing, and a scope outside what the client may use on that resource answers `invalid_scope` (RFC 6749).
- Service tokens write no row to `identity_oidc_tokens`; nothing can revoke them, so they end with their lifetime.

## Token exchange

```http
POST /connect/token
grant_type=urn:ietf:params:oauth:grant-type:token-exchange
subject_token=<the person's access token>
subject_token_type=urn:ietf:params:oauth:token-type:access_token
resource=https://files.example.com/
client_id=ocharo-editorial&client_secret=…
```

- The caller must be the subject token's presenter or audience, so an app exchanges only tokens issued to it.
- The subject token must be a person's own: a service token or one already exchanged answers `invalid_grant`.
- An ended sign-in session answers `invalid_grant`, as does a token with no `sid` on an instance with sessions.
- The result never outlives the session; an issued token lives out its few minutes unless a [signal](revocation-signals.md) stops it.
- `actor_token` and a `requested_token_type` other than `access_token` are refused.

## Client authentication

- The client's secret is the default; a client may name a `JsonWebKeySet` of public keys instead and sign `private_key_jwt`
  assertions (developer and orchestrator, 2026-11-05). The boot refuses both on one client, and refuses a private key in the set.
- The method belongs to each client's registration. On the caller it is one setting of the be sdk's handlers,
  `ClientSecret` or `ClientPrivateKey`, so switching a client changes configuration, never calling code.
- A BFF signs people in with its secret, so a client with the code flow stays on it for now.
- Wheelhouse generates no client keys yet: a key pair is made by hand, its private half kept in the vault.

## Resource side

```csharp
services.AddJwtBearerAuthentication(o =>
{
    o.Audience = "https://files.example.com/";
    o.TrustedIssuers.Add(new TrustedIssuer { Issuer = "https://id.example.com/", Partition = "ocharo" }); // discovery under the issuer
    o.UseTrustedIssuerResolver<FilesTenantIssuerResolver>();   // optional: issuers from a store, no restart
});
services.AddCurrentPrincipal();

app.MapGet("/files", …).RequireAuthorization(policy => policy.RequireScope("files.read"));
```

- `ICurrentPrincipal` (kernel) answers person, service or key over one normalizer, so a policy never branches by scheme.
- An issuer's keys validate only that issuer's tokens; an unknown issuer answers `401`.

## Caller side

```csharp
services.AddHttpClient("desk").AddOAuth2ClientCredentials(o => { o.TokenEndpoint = …; o.ClientId = …; o.ClientSecret = …; o.Resource = "https://desk.example.com/"; });
services.AddHttpClient("files").AddOAuth2TokenExchange(o => { o.TokenEndpoint = …; o.ClientId = …; o.ClientSecret = …; o.Resource = "https://files.example.com/"; });
```

- Client credentials cache per audience; exchange tokens per session and audience. Scope stays out of the key.
- `AddIdentityBff` makes a BFF's exchange read the subject token from its session; an API reads the request's bearer token.
- The session key holds `sid` and the active `org_id`, so switching organization takes a new token.

## Tests

- `FakeTokenIssuer` in `WoW2.Sdk.Beta.Be.Identity.Testing` signs person, service and exchanged tokens, so a consumer tests with no Identity.

## Differences from T8

| T8 | Here | Why |
|---|---|---|
| C3 `Identity:Resources` keyed by name | `Identity:Server:Resources` as a list with `Name` | beside `Identity:Server:Clients`, the same shape |
| C3 `https://files.example` | `https://files.example/` | OpenIddict compares `AbsoluteUri` exactly |
| C2 `azp` | `client_id` | OpenIddict and RFC 9068 write it on access tokens; the normalizer reads both |
| C6 a person's token may be re-exchanged | refused once it has `act` | no case yet; OpenIddict refuses a caller that isn't its presenter or audience anyway |
| C3, C6 `scope` ⊆ the client's scopes, as asked | every scope the client may use on the resource | developer, 2026-11-05: a list of actions in one token, never a token per action |
| C8 `AddIdentityValidation` | options on `AddJwtBearerAuthentication` | the orchestrator's item 4; one registration |

## Later

- [Revocation signals](revocation-signals.md) stop a token before it expires; exchanged tokens keep `sid` for that.
- Wheelhouse generating client key pairs.
- A BFF authenticating with `private_key_jwt`.

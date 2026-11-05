# Identity.OidcServer

The OpenID Connect provider over the Form 1 module: one registration (`AddOidcServer`) on the builder
`AddUserAccounts` returns, one endpoint map (`MapOidcServerEndpoints`). OpenIddict 7.7.2 carries the protocol (D1, T1);
our code decides who signs in and what their tokens say. Slices I17–I18.

## Quick start

```csharp
services.AddUserAccounts().AddSignIn().AddSessions().AddOrganizations().AddAccountEndpoints()
    .AddOidcServer(server => server.SignInPath = "/sign-in");   // the host section Identity:Server applies last
services.AddCookieAuthentication();

app.UseAuthentication();
app.MapGroup("/api/account").MapUserAccountEndpoints();
app.MapOidcServerEndpoints();
```

```jsonc
"Identity": {
  "Server": {
    "Issuer": "https://identity.example.com/",   // required on a deployed boot; null derives it per request on a local run
    "SignInPath": "/sign-in",
    "AuthorizationCodeLifetime": "00:01:00",
    "AccessTokenLifetime": "00:10:00",
    "IdentityTokenLifetime": "00:05:00",
    "RefreshTokenLifetime": "12:00:00",           // absolute: rotation never extends it
    "ServiceTokenLifetime": "00:05:00",           // client credentials and token exchange; at most 10 minutes
    "Keys": { "Signing": [ "…" ], "Encryption": [ "…" ] },   // ../Keys/keys.md
    "Clients": [
      {
        "ClientId": "wheelhouse",
        "ClientSecret": "<from the vault>",
        "RedirectUris": [ "https://wheelhouse.example.com/signin-oidc" ],
        "PostSignOutRedirectUris": [ "https://wheelhouse.example.com/signout-callback-oidc" ],
        "Audience": "wheelhouse",                  // null uses the client id
        "PermissionNamespace": "wheelhouse"        // null carries every permission of the role
      }
    ],
    "Resources": []                                // see Service tokens
  }
}
```

## Endpoints

| Path | Served by | Does |
|---|---|---|
| `/.well-known/openid-configuration` · `/.well-known/jwks` | OpenIddict | discovery (code flow, S256, the claims below) and the signing keys |
| `/connect/authorize` | pass-through | reads the cookie session; none, `prompt=login` or an exceeded `max_age` redirects to `SignInPath?returnUrl=…`, where the return URL trades `prompt` and `max_age` for `fresh_sign_in`, the request's instant sealed with Data Protection: back from the sign-in page, the request passes only for a session signed in after it (a missing or forged marker asks again). `prompt=none` answers `login_required`; ties the grant to the session's `sid` |
| `/connect/token` | pass-through | code and refresh grants re-check the person (exists, same stamp) and the session behind the grant, and write fresh claims; a lockout does not end a grant. Client credentials and token exchange issue a service token (below) |
| `/connect/introspect` | OpenIddict, with `OidcLiveTokenCheckHandler` | RFC 7662 for a client with `CanIntrospect`: `active: false` once the person is gone, their session ended, or their membership or role in the token's organization differs |
| `/connect/userinfo` | pass-through | the current profile and organization claims for the access token, while its session lives |
| `/connect/endsession` | pass-through | with a valid `id_token_hint`, ends the cookie and its session (when they are the hint's person) and revokes the app's authorizations and tokens for that person; a `POST` from this origin (`Sec-Fetch-Site: same-origin`, or a matching `Origin`) ends the cookie and its session without a hint; anything else, such as a bare `GET` another site sends, ends nothing; every case returns to the validated `post_logout_redirect_uri` |

## Claims

`OidcClaimMapper` is the one place that writes a person's claims and sends each to its tokens.

| Claim | ID token | Access token | Source |
|---|---|---|---|
| `sub` | ✓ | ✓ | the user id |
| `sid` | ✓ | ✓ | the sign-in session the grant came from (`SessionClaimTypeConstants.SessionId`); set at authorization, kept by refreshes |
| `name` | with `profile` | with `profile` | the user name |
| `email`, `email_verified` | with `email` | with `email` | the account |
| `amr` | ✓ | ✓ | how the session signed in (`pwd`, `hwk`, `otp`, `mfa`; `AuthenticationMethodConstants`), an array |
| `auth_time` | ✓ | — | when the session signed in: the session row's start, which a sliding reissue of the cookie never moves (the cookie's issue time on a host without sessions) |
| `org_id` | ✓ | ✓ | the active organization |
| `org_role` | ✓ | ✓ | `{org}:{role}` in the active organization |
| `org_permission` | ✓ | ✓ | `{org}:{permission}` per permission of that role, kept to the client's namespace, an array |

- Active organization: the one the grant already carries while the person still belongs to it, else the earliest
  membership (ties by organization id); a person without memberships gets no organization claims.
- The security stamp rides inside codes and refresh tokens only; a rotated stamp makes the next refresh fail
  `invalid_grant`.
- Userinfo answers neither `amr`, `auth_time` nor `sid`.

## Sessions

With the sessions slice (`AddSessions`, `../../Module/Core/Sessions/sessions.md`), every grant hangs off the sign-in
session that made it:

- the authorize endpoint creates an ad-hoc authorization whose `sid` property names the cookie's session, and the
  code, refresh and access tokens are issued under it; both tokens carry `sid`
- the token endpoint refuses a grant whose session ended (`invalid_grant`), userinfo a token whose session ended
  (`invalid_token`)
- revoking a session (the person's session list, the account API's sign-out, end-session, or a stamp rotation when next
  seen) runs `OidcSessionRevocationHandler`: it revokes that session's authorizations and every token under them, so
  its refresh chains end at once
- a grant without `sid` (issued before sessions, or by a host without the slice) passes the session check

## Service tokens

Spec, claims and refusals: [service tokens](../../../../../../product/features/service-tokens.md). `OidcServiceTokenGrants` serves both grants.

- `Identity:Server:Resources` is the registry: a name, the URI that becomes the only `aud`, the `{service}.{action}` scopes
  and an optional permission namespace. OpenIddict validates `resource` and the client's permission; `aud` comes from here.
- A client names what it may do: `Grants`, `Resources` (as itself), `ExchangeResources` (for a person), `Scopes`, and
  optionally `OrganizationId`. Empty `Grants` means the code flow alone, as every client had before.
- `OidcClientSeeder` writes the grant, `rsrc:` and `scp:` permissions, and the `service_access` property the token endpoint
  reads to tell which grant reaches which resource.
- `OidcServiceTokenEntryHandler` keeps these tokens out of `identity_oidc_tokens`; they end with `ServiceTokenLifetime`.
- A client authenticates with `ClientSecret`, or with `JsonWebKeySet` instead (`private_key_jwt`, public keys only). The
  assertion's `aud` is the issuer and its `typ` is `client-authentication+jwt`, as OpenIddict 7 requires.
- A boot refuses a resource URI not written as `Uri.AbsoluteUri` (`https://files.example/`, with its slash), a scope that
  is not `{service}.{action}`, a client whose grants, resources and scopes disagree, and a lifetime above 10 minutes.

## Signals

Spec: [revocation signals](../../../../../../product/features/revocation-signals.md). `../Signals/`:

- `OidcSignalRelay` hears `ISessionRevocationHandler` and `IAccessChangeHandler` and puts `SessionEndedEvent` and
  `AccessChangedEvent` on the local bus, off the request, when a client or resource names a `SignalUrl`.
- `OidcSignalForwarder` carries each event to `IWebhookPublisher`; `OidcSignalSubscriptionRepository` reads the targets from
  this registry, one per URL.
- `OidcSignalSignatureIssuer` signs each delivery with the active token key: a `webhook+jwt` bound to the target URL and the
  hash of the body, under the `key-signed` scheme, so a receiver needs no shared secret.
- A signal URL needs a pinned `Issuer`. Lost after its retries, a signal is logged and dropped; a token's lifetime is the limit.

## Clients

- `Identity:Server:Clients` is seeded at startup by `OidcClientSeeder`: created when new, updated when changed, left
  alone when unchanged (a matching secret keeps its stored hash), and deleted with its grants when it leaves
  configuration. Clients registered another way are never touched.
- Every client is confidential and first-party (no consent). One that signs people in has the code flow with PKCE
  required, refresh tokens, the `openid`, `profile`, `email` and `offline_access` scopes, exact redirect and post-sign-out
  URIs (HTTPS, or HTTP on a loopback host) and its own audience. Secrets are stored as OpenIddict's hash.
- The boot refuses a client without exactly one of a secret and a key set, a key set holding a private key, one signing people in without a redirect URI, a duplicate id, a loose URI, a
  blank audience, a namespace holding `:`, a non-positive lifetime or a sign-in path that is not local.

## Notes

- Codes and refresh tokens are encrypted and stored (`identity_oidc_tokens`), so each redeems once; access tokens are
  unencrypted JWTs.
- OpenIddict stores token types as URNs (`OpenIddictConstants.TokenTypeIdentifiers`), such as
  `urn:ietf:params:oauth:token-type:refresh_token`.
- OpenIddict's `SetClaim(type, string, string)` takes an issuer, not a value type; typed values use the `long` and
  `bool` overloads.
- A request failing before its redirect URI is checked (no `code_challenge`, `plain`) gets its error in the response
  body, not a redirect.

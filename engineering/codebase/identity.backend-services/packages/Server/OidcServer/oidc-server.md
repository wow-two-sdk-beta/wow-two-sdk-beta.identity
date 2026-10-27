# Identity.OidcServer

The OpenID Connect provider over the Form 1 module: one registration (`AddOidcServer`) on the builder
`AddUserAccounts` returns, one endpoint map (`MapOidcServerEndpoints`). OpenIddict 7.7.2 carries the protocol (D1, T1);
our code decides who signs in and what their tokens say. Slices I17–I18.

## Quick start

```csharp
services.AddUserAccounts().AddSignIn().AddOrganizations().AddAccountEndpoints()
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
    "Keys": { "Signing": [ "…" ], "Encryption": [ "…" ] },   // ../Keys/keys.md
    "Clients": [
      {
        "ClientId": "wheelhouse",
        "ClientSecret": "<from the vault>",
        "RedirectUris": [ "https://wheelhouse.example.com/signin-oidc" ],
        "PostLogoutRedirectUris": [ "https://wheelhouse.example.com/signout-callback-oidc" ],
        "Audience": "wheelhouse",                  // null uses the client id
        "PermissionNamespace": "wheelhouse"        // null carries every permission of the role
      }
    ]
  }
}
```

## Endpoints

| Path | Served by | Does |
|---|---|---|
| `/.well-known/openid-configuration` · `/.well-known/jwks` | OpenIddict | discovery (code flow, S256, the claims below) and the signing keys |
| `/connect/authorize` | pass-through | reads the cookie session; none, `prompt=login` or an exceeded `max_age` redirects to `SignInPath?returnUrl=…` (without `prompt` and `max_age`), `prompt=none` answers `login_required` |
| `/connect/token` | pass-through | code and refresh grants only; re-checks the person (exists, not locked out, same stamp) and writes fresh claims |
| `/connect/userinfo` | pass-through | the current profile and organization claims for the access token |
| `/connect/endsession` | pass-through | ends the cookie session; with `id_token_hint`, revokes the app's authorizations and tokens for that person; returns to the validated `post_logout_redirect_uri` |

## Claims

`OidcClaimMapper` is the one place that writes a person's claims and sends each to its tokens.

| Claim | ID token | Access token | Source |
|---|---|---|---|
| `sub` | ✓ | ✓ | the user id |
| `name` | with `profile` | with `profile` | the user name |
| `email`, `email_verified` | with `email` | with `email` | the account |
| `amr` | ✓ | ✓ | how the session signed in (`pwd`, `hwk`, `otp`, `mfa`; `AuthenticationMethodConstants`), an array |
| `auth_time` | ✓ | — | when the session signed in |
| `org_id` | ✓ | ✓ | the active organization |
| `org_role` | ✓ | ✓ | `{org}:{role}` in the active organization |
| `org_permission` | ✓ | ✓ | `{org}:{permission}` per permission of that role, kept to the client's namespace, an array |

- Active organization: the one the grant already carries while the person still belongs to it, else the earliest
  membership (ties by organization id); a person without memberships gets no organization claims.
- The security stamp rides inside codes and refresh tokens only; a rotated stamp makes the next refresh fail
  `invalid_grant`. `sid` arrives with sessions (I16).

## Clients

- `Identity:Server:Clients` is seeded at startup by `OidcClientSeeder`: created when new, updated when changed, left
  alone when unchanged (a matching secret keeps its stored hash), and deleted with its grants when it leaves
  configuration. Clients registered another way are never touched.
- Every client is confidential and first-party (no consent): code flow with PKCE required, refresh tokens, the
  `openid`, `profile`, `email` and `offline_access` scopes, exact redirect and post-logout URIs (HTTPS, or HTTP on a
  loopback host), its own audience. Secrets are stored as OpenIddict's hash.
- The boot refuses a client without a secret or redirect URI, a duplicate id, a loose URI, a blank audience, a
  namespace holding `:`, a non-positive lifetime or a sign-in path that is not local.

## Notes

- Codes and refresh tokens are encrypted and stored (`identity_oidc_tokens`), so each redeems once; access tokens are
  unencrypted JWTs.
- OpenIddict stores token types as URNs (`OpenIddictConstants.TokenTypeIdentifiers`), such as
  `urn:ietf:params:oauth:token-type:refresh_token`.
- OpenIddict's `SetClaim(type, string, string)` takes an issuer, not a value type; typed values use the `long` and
  `bool` overloads.
- A request failing before its redirect URI is checked (no `code_challenge`, `plain`) gets its error in the response
  body, not a redirect.

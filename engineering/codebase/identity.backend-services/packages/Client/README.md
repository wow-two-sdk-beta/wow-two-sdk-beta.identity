# WoW2.Sdk.Beta.Be.Identity.Client

Forms 2 and 3 of Identity: a product signs in against a running Identity instance. Today it holds the BFF sign-in: an
API and its SPA on one origin sign people in by the OpenID Connect code flow with PKCE, keep the tokens server-side and
give the SPA session routes.

```bash
dotnet add package WoW2.Sdk.Beta.Be.Identity.Client --prerelease
```

```csharp
builder.Services.AddIdentityBff();                      // Identity:Client — Authority, ClientId, ClientSecret, …

app.UseIdentityBff();                                   // authentication, the X-Csrf-Token check, authorization
app.MapIdentityBffEndpoints();                          // GET /api/identity/session · GET /api/identity/sign-in · POST /api/identity/sign-out
app.MapGroup("/api/orgs/{organizationId:guid}/desks")
    .RequireRouteOrganization()                         // 404 for another organization
    .RequirePermission("desk:edit");                    // 403 without org_permission desk:edit
```

- Settings: `Identity:Client` — `Authority`, `ClientId`, `ClientSecret`, `Scopes` (default `openid profile email phone
  offline_access`), `RoutePrefix` (`/api/identity`), `ApiPathPrefixes` (`/api`), `PostSignOutRedirectPath` (`/`),
  `SessionLifetime` (12 h, only while the instance names no end), `AccessTokenRefreshMargin` (1 min), `CookieName`,
  `Environment`; code configuration wins.
- Session end: the BFF session ends with the instance's sign-in session, whose end each token response names
  (`refresh_token_expires_in`); a sliding instance session moves it, and the cookie is reissued to follow.
- Add the client at the instance with the redirect URI `{origin}/api/identity/callback` and the post-sign-out URI
  `{origin}/` (`Identity:Server:Clients:N:RedirectUris` and `PostSignOutRedirectUris`).
- The session routes sit under the API prefix: `session` and `sign-out` answer as API calls (`sign-out` needs
  `X-Csrf-Token`), while `sign-in` and `callback` redirect, as a browser navigates to them.
- The cookie carries only the session reference; the tokens and claims live in `IBffSessionRepository` (the
  distributed cache by default), keyed on the instance's `sid`, encrypted with Data Protection. Several instances
  share a distributed cache and a Data Protection key ring.
- A sign-in callback that fails lands back at `returnUrl` with `signInError=callback_failed`; for 10 s after it the
  sign-in route and page redirects answer a 401 problem (`code` `SignInFailed`) instead of starting the sign-in again,
  so a misconfigured client ends at an error rather than in a redirect loop.

## Deployed hosts: persist the sessions and the key ring

The defaults (`AddDistributedMemoryCache`, `AddDataProtection` with no key store) hold for a local run only: every
restart or deploy forgets the sessions and the keys, so every cookie, CSRF token and sign-in in flight stops working
and everyone signs in again. A deployed host registers both before `AddIdentityBff`:

```csharp
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisConnection);   // or any shared IDistributedCache
builder.Services.AddDataProtection()
    .SetApplicationName("ocharo")                                                     // one name across instances
    .PersistKeysToDbContext<AppDbContext>();                                          // or PersistKeysToStackExchangeRedis, a volume
builder.Services.AddIdentityBff();
```

- A deployed boot (`DEPLOY_ENVIRONMENT` set) logs a warning for each one still on its default; it never refuses to boot.
- Protect the persisted ring at rest (`ProtectKeysWithCertificate`) where the store is shared.
- API requests answer 401, 403 and 404 RFC 7807 problems with a `code`; page requests are redirected to sign in.

Namespace: `WoW.Two.Sdk.Beta.Be.Identity.Bff`.

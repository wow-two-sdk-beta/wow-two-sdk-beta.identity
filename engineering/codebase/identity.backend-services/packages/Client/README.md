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
  `SessionLifetime` (12 h), `AccessTokenRefreshMargin` (1 min), `CookieName`, `Environment`; code configuration wins.
- Add the client at the instance with the redirect URI `{origin}/api/identity/callback` and the post-sign-out URI
  `{origin}/` (`Identity:Server:Clients:N:RedirectUris` and `PostSignOutRedirectUris`).
- The session routes sit under the API prefix: `session` and `sign-out` answer as API calls (`sign-out` needs
  `X-Csrf-Token`), while `sign-in` and `callback` redirect, as a browser navigates to them.
- The cookie carries only the session reference; the tokens and claims live in `IBffSessionRepository` (the
  distributed cache by default), keyed on the instance's `sid`, encrypted with Data Protection. Several instances
  share a distributed cache and a Data Protection key ring.
- API requests answer 401, 403 and 404 RFC 7807 problems with a `code`; page requests are redirected to sign in.

Namespace: `WoW.Two.Sdk.Beta.Be.Identity.Bff`.

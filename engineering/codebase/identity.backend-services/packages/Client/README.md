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
app.MapIdentityBffEndpoints();                          // GET /auth/session · GET /auth/login · POST /auth/logout
app.MapGroup("/api/orgs/{organizationId:guid}/desks")
    .RequireRouteOrganization()                         // 404 for another organization
    .RequirePermission("desk:edit");                    // 403 without org_permission desk:edit
```

- Settings: `Identity:Client` — `Authority`, `ClientId`, `ClientSecret`, `Scopes` (default `openid profile email phone
  offline_access`), `RoutePrefix` (`/auth`), `ApiPathPrefixes` (`/api`), `PostLogoutRedirectPath` (`/`),
  `SessionLifetime` (12 h), `AccessTokenRefreshMargin` (1 min), `CookieName`, `Environment`; code configuration wins.
- Register the client at the instance with the redirect URI `{origin}/auth/callback` and the post-logout URI
  `{origin}/`.
- The cookie carries only the session reference; the tokens and claims live in `IBffSessionRepository` (the
  distributed cache by default), keyed on the instance's `sid`, encrypted with Data Protection. Several instances
  share a distributed cache and a Data Protection key ring.
- API requests answer 401, 403 and 404 RFC 7807 problems with a `code`; page requests are redirected to sign in.

Namespace: `WoW.Two.Sdk.Beta.Be.Identity.Bff`.

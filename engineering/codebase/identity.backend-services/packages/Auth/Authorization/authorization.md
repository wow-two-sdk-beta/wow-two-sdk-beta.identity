# Identity.Authorization

Provider-agnostic ASP.NET Core authorization primitives for self-hosted, single-admin (or small-allowlist) APIs.

## Primitives

- **Principal allowlist** — `AllowlistRequirement` + `AllowlistAuthorizationHandler`, configured via `AllowlistOptions`. Gates to an allowed set keyed on a claim.
  - `ClaimType` default `"wt:username"` (seam: `Identity/Claims` `NormalizedClaimTypeConstants.Username`).
  - **Open-when-empty:** `Allowed` empty ⇒ any *authenticated* principal passes. Non-empty ⇒ principal must carry a `ClaimType` claim whose value ∈ `Allowed` (case-insensitive by default).
- **Default-deny** — `AddDefaultDenyAuthorization(scheme, withAllowlist)` builds `RequireAuthenticatedUser()` (+ allowlist) on **one named scheme**, registers it under `PolicyName` (`"DefaultDeny"`) **and** sets it as `FallbackPolicy` ⇒ endpoints without `[Authorize]`/`[AllowAnonymous]` are denied by default.
  - Single named scheme (the cookie) → clean **401**, not an external-IdP **302**.
  - `/health`-style anonymous endpoints **must** use `[AllowAnonymous]`.

- **Scope and audience** — `RequireScope` and `RequireAudience` on `AuthorizationPolicyBuilder`, over the public `ClaimsPrincipal` extensions `HasScope` and `HasAudience`. Each implies an authenticated user and needs no handler registration (`RequireAssertion`): no token answers **401**, a token without it **403**.
  - `RequireScope`: repeated calls all apply. A scope comes from `scope` or `scp` claims, split on spaces and compared ordinally, so one space-delimited JWT claim and one-claim-per-scope API keys both work, and a policy never branches by scheme.
  - `RequireAudience`: for an endpoint narrower than the host, which already checks its audiences at validation.

```csharp
app.MapGet("/files", GetFiles).RequireAuthorization(policy => policy.RequireScope("files.read"));
app.MapPost("/admin/purge", Purge).RequireAuthorization(policy => policy.RequireAudience("https://files.example.com/admin").RequireScope("files.admin"));
```

- **Live token** — `RequireLiveToken` on `AuthorizationPolicyBuilder`, with `AddLiveTokenCheck` (`Identity/Signals`). For named dangerous actions only: it posts the request's bearer token to `/connect/introspect` (RFC 7662), on every request with nothing cached, while every other request stays offline.
  - Passes only on `active: true`. An inactive token, an unreachable or refusing endpoint, an unreadable answer and a caller with no bearer token all answer **403**; an anonymous caller **401**.
  - The requirement handles itself, so a policy that isn't used costs no registration. A used one with no `AddLiveTokenCheck` throws, since that's a wiring mistake.
  - The client authenticates as at the token endpoint (`OAuth2ClientAuthentication`), and needs `CanIntrospect` at the instance. HTTPS unless loopback, checked at boot.

```csharp
app.MapPost("/files/purge", Purge).RequireAuthorization(policy => policy.RequireLiveToken().RequireScope("files.admin"));
```

**Compose:** `withAllowlist: true` makes the fallback require *authenticated* **and** *allowlisted*; empty allowlist keeps it authenticated-only.

## Canonical wiring order

```csharp
builder.Services
    .AddCookieAuthentication(o => o.ChallengeMode = AuthChallengeMode.Api) // app cookie (clean 401)
    .Add{Provider}Authentication(/* ... */)                               // external IdP (OAuth/OIDC)
    .AddClaimNormalization()                                              // → wt:username (Identity/Claims)
    .AddPrincipalAllowlist(o =>
    {
        // empty Allowed ⇒ OPEN (single admin). Add usernames to lock down:
        // o.Allowed.Add("sultonbek");
    })
    .AddDefaultDenyAuthorization(CookieAuthenticationDefaults.AuthenticationScheme, withAllowlist: true);
```

`AddClaimNormalization` is the planned `Identity/Claims` seam emitting the `wt:username` claim the allowlist keys on.

See also: `Identity/Cookies` (named scheme + `AuthChallengeMode.Api`), `Authorization.spec.md`, `Authorization.standard.md`.

# WoW.Two.Sdk.Beta.Be.Identity.Cookies

> Cookie auth with secure defaults: `HttpOnly`, `SameSite=Lax`, `SecurePolicy=Always`.

## Install

```
dotnet add package WoW.Two.Sdk.Beta.Be.Identity.Cookies
```

## Register

```csharp
builder.Services.AddCookieAuthentication(o =>
{
    o.ExpireTimeSpan = TimeSpan.FromHours(24);
    o.LoginPath = "/auth/login";
    o.Mode = AuthChallengeMode.Api;   // omit for MVC (default)
});
```

## `Mode` — challenge style

| `AuthChallengeMode` | On 401/403 | Use for |
|---|---|---|
| `Mvc` *(default)* | 302 redirect to `LoginPath` / access-denied page | Server-rendered MVC |
| `Api` | Raw `401` / `403`, no redirect | SPA + API backends |

**Why:** a SPA's `fetch` auto-follows 3xx → never sees the 302, so it needs a raw 401/403 to render its own sign-in; MVC wants the 302 to a server-rendered login page. Secure defaults apply in both modes.

## `Environment` — one cookie per deploy environment

Cookies ignore the port, so dev and prod served from one host name overwrite each other's sign-in. Name the
environment and every one but `PrimaryEnvironment` (`prod` by default) adds its own suffix:

```csharp
builder.Services.AddCookieAuthentication(o =>
{
    o.CookieName = ".wheelhouse.auth";
    o.Environment = builder.Configuration.GetDeployEnvironment(); // DEPLOY_ENVIRONMENT; null on a local run
});
```

| `Environment` | Cookie |
|---|---|
| `null`, empty, `prod` | `.wheelhouse.auth` |
| `dev` (any case, trimmed) | `.wheelhouse.auth.dev` |
| not ASCII letters and digits alone (`dev;path=/`) | `.wheelhouse.auth` |

`EnvironmentCookieName.For(name, environment)` gives the same name to code that reads the cookie by name.

## See also

- `CookiesServiceCollectionExtensions.cs` — `AddCookieAuthentication` + `CookieAuthOptions`.

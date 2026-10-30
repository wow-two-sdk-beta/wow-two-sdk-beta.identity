# WoW.Two.Sdk.Beta.Be.Identity.OAuth.Endpoints

> The browser sign-in surface over one OAuth provider: begin sign-in, read the signed-in user, sign out. A SPA in API
> cookie mode (`AuthChallengeMode.Api`) needs nothing else.

## Map

```csharp
// Register the provider only when configured, so the host boots without its client id and secret.
if (gitHub.IsConfigured)
    builder.Services.AddAuthentication().AddGitHubAuthentication(gitHub.ClientId, gitHub.ClientSecret,
        o => o.CallbackPath = "/api/identity/callback", "user:email");

app.MapGroup("/api/identity").MapOAuthSignInEndpoints("GitHub", o =>
    o.NotConfiguredDetail = "GitHub sign-in is not configured. Set Identity:GitHub:ClientId and Identity:GitHub:ClientSecret.");
```

| Route | Access | Answers |
|---|---|---|
| `GET sign-in?returnUrl=` | anonymous | challenge → the provider, back to `returnUrl` when local, else `/`; 503 problem with `NotConfiguredDetail` while the scheme is not registered |
| `GET me` | anonymous | 200 `{ login, name, avatar }` from the normalized claims; 401 problem when anonymous |
| `POST sign-out` | default policy | signs out of `SignOutScheme` (the cookie scheme), 204 |

- `login`: `wt:username`, else the name claim; `name`: `wt:display_name`, else the login; `avatar`: `wt:avatar` or `null`.
- `sign-out` takes the default policy, not the fallback: a signed-in user outside an allowlist can still sign out.
- The provider's callback (`CallbackPath`) stays its handler's own route; point it inside the same group.
- Needs `AddClaimNormalization()` for `me` to read the `wt:*` claims.

## Options

| `OAuthSignInEndpointOptions` | Default |
|---|---|
| `NotConfiguredDetail` | `Sign-in is not configured.` |
| `SignOutScheme` | `CookieAuthenticationDefaults.AuthenticationScheme` |

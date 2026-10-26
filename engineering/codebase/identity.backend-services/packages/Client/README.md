# WoW2.Sdk.Beta.Be.Identity.Client

Forms 2 and 3 of Identity: a product signs in against a running Identity instance. Today it holds the OIDC client
(Authorization Code + PKCE, `AddOpenIdConnectAuthentication`), paired with `AddCookieAuthentication` from
`WoW2.Sdk.Beta.Be.Identity.Auth`.

```bash
dotnet add package WoW2.Sdk.Beta.Be.Identity.Client --prerelease
```

```csharp
services.AddCookieAuthentication();
services.AddOpenIdConnectAuthentication(options => { options.Authority = "https://id.example.com"; options.ClientId = "app"; });
```

Namespace unchanged from the be sdk: `WoW.Two.Sdk.Beta.Be.Identity.Oidc`.

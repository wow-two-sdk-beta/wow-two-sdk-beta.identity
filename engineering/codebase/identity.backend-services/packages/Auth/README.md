# WoW2.Sdk.Beta.Be.Identity.Auth

The store-less part of Identity: what a product uses with no Identity store or service.

- `AddCookieAuthentication`, `AddApiKeyAuthentication`, the allowlist and default-deny authorization
- the guest session and `CookieCurrentUserService` (the kernel's `ICurrentUserService` over the signed-in or guest user)
- `AddJwtBearerAuthentication` with audience and organization policies, and HMAC `ITokenIssuer` issuance

Module and Client build on it, so a Form 1 or Form 3 product gets it transitively; an M0 product or an API resource
takes it alone.

```bash
dotnet add package WoW2.Sdk.Beta.Be.Identity.Auth --prerelease
```

Namespaces are unchanged from the be sdk: `WoW.Two.Sdk.Beta.Be.Identity.{Cookies,ApiKeys,Authorization,Guest,Jwt,CurrentUser}`.

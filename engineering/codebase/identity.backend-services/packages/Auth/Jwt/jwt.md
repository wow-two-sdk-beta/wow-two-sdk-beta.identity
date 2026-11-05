# WoW.Two.Sdk.Beta.Be.Identity.Jwt

> JWT bearer authentication with issuer, audience, lifetime, algorithm and verification keys enforced: one issuer, or several trusted ones.

## Install

```
dotnet add package WoW.Two.Sdk.Beta.Be.Identity.Jwt
```

## Usage

### Symmetric key

```csharp
builder.Services.AddJwtBearerAuthentication(o =>
{
    o.Issuer = "https://my-issuer";
    o.Audience = "my-api";
    o.SymmetricKey = builder.Configuration["Jwt:Key"]!;
    o.Algorithm = SecurityAlgorithms.HmacSha256;
});

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
```

### OpenID Connect metadata

```csharp
builder.Services.AddJwtBearerAuthentication(o =>
{
    o.Issuer = "https://login.microsoftonline.com/{tenant}/v2.0";
    o.Audience = "api://my-api";
    o.MetadataAddress = new Uri("https://login.microsoftonline.com/{tenant}/v2.0/.well-known/openid-configuration");
    o.Algorithm = SecurityAlgorithms.RsaSha256;
});
```

Configure exactly one of `SymmetricKey` and `MetadataAddress`. Remote metadata requires HTTPS. A local HTTP identity provider requires the explicit `IsInsecureMetadataAllowedInDevelopment` escape.

`Audiences` holds more audiences accepted beside `Audience`: a service often accepts its own person tokens and its resource URI.

### Trusted issuers

A resource that takes tokens from several issuers lists them. Service tokens: `product/features/service-tokens.md`.

```csharp
builder.Services.AddJwtBearerAuthentication(o =>
{
    o.Audience = "https://files.example.com/";
    o.TrustedIssuers.Add(new TrustedIssuer { Issuer = "https://id.example.com/", Partition = "ocharo" });
    o.UseTrustedIssuerResolver<FilesTenantIssuerResolver>(); // optional: issuers from a store, no restart
});
```

| `TrustedIssuer` | Means |
|---|---|
| `Issuer` | compared exactly with `iss` |
| `MetadataAddress` | discovery; null is `{Issuer trimmed}/.well-known/openid-configuration` |
| `SigningKeys` | static keys instead of discovery: a fake issuer, a test host |
| `Audiences` | what its tokens may name; empty is the audiences of `JwtOptions` |
| `Partition` | the tenant stamped on the principal as `partition` |

- Any listed issuer or a resolver turns this mode on. The boot then refuses `Issuer`, `SymmetricKey` and `MetadataAddress`, and needs an audience in `JwtOptions` or on every listed issuer; a resolver alone needs one in `JwtOptions`.
- The list answers first. The resolver is asked only about an issuer the list lacks, in its own scope, before any signature is checked: it looks in a store and caches, and it never fetches what the value names.
- An unknown issuer answers `401` with no network call. A token validates only against the keys and audiences of the issuer it names, so one issuer's keys never validate another's tokens.
- Only the asymmetric algorithms (RS, PS, ES) pass, whatever `Algorithm` says; the boot refuses a symmetric key in `SigningKeys`.
- Discovery keeps one configuration manager per metadata address: 12-hour automatic refresh, at most one asked refresh per 5 minutes, HTTPS unless `IsInsecureMetadataAllowedInDevelopment`. `BackchannelHttpHandler` answers it in a test host.
- A token whose `kid` no held key matches asks for a refresh and retries once. Tokens arriving meanwhile wait for the same refresh, at most 5 seconds; inside the 5 minutes the retry gets no new keys, so a stream of forged `kid`s costs one fetch.
- The principal loses any `partition` the token carries and gets the issuer's `Partition` when set: a token's own claims never choose the tenant. One-issuer mode stamps nothing and leaves a token's claims as they are.
- A resolver or discovery failure answers `401`, with the reason in the host's log.

Tests stand in for all of this with `FakeTokenIssuer` in `WoW2.Sdk.Beta.Be.Identity.Testing`.

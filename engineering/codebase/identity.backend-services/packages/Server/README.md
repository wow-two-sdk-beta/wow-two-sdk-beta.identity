# WoW2.Sdk.Beta.Be.Identity.Server

The OpenID Connect provider the Identity service runs over the Form 1 module: OpenIddict 7.7.2 with its EF Core stores
on the Module's `IdentityDbContext`, the pass-through endpoints on the Module's sign-in session, the client registry
seeded from configuration, certificates from the settings (inline PEM bundles or file paths) checked at boot, and the
instance's Data Protection ring in its database. Products do not reference it; the Identity service does. The
back-channel sender and exchange grants (I19) come later.

```csharp
services.AddUserAccounts(configureDatabase: database => database.SchemaOwner = IdentitySchemaOwnerConstants.Service)
    .AddArgon2Passwords()
    .AddSignIn()
    .AddSecurityStampValidation()
    .AddSessions()                                       // sid in the cookie and the tokens; revoking ends the grants
    .AddSignInLog()
    .AddOrganizations()
    .AddAccountEndpoints()
    .AddOidcServer()                                     // Identity:Server binds over any code options
    .AddDataProtectionRing();                            // Identity:DataProtection: the ring's certificates

services.AddCookieAuthentication();                      // the sign-in session the authorize endpoint reads

app.UseAuthentication();
app.MapGroup("/api/account").MapUserAccountEndpoints(); // POST login?useCookies=true starts the session
app.MapOidcServerEndpoints();                           // /connect/{authorize,token,userinfo,endsession}
```

| Folder | Holds | Lead doc |
|---|---|---|
| `OidcServer/` | `AddOidcServer`, `MapOidcServerEndpoints`, `OidcServerOptions` and `OidcClientOptions`, the claim mapping, the client seeding and the session revocation handler | [oidc-server.md](OidcServer/oidc-server.md) |
| `Keys/` | `ISigningKeySource`, `SettingsSigningKeySource`, `CertificateSetting`, `SigningKeyValidator`, the per-token key pick, `SigningCertificateGenerator`, the `keys generate` command | [keys.md](Keys/keys.md) |
| `DataProtection/` | `AddDataProtectionRing`, `DataProtectionRingOptions`, the database repository and the certificate wrapping | [data-protection.md](DataProtection/data-protection.md) |

- Grants: authorization code with PKCE (S256 only) and rotating refresh tokens; no implicit, password or client
  credentials grant.
- Tokens: access tokens are unencrypted JWTs a resource server verifies offline against the JWKS; codes and refresh
  tokens are encrypted. Lifetimes default to code 60 s, access 10 min, ID 5 min, refresh 12 h absolute. With sessions,
  both tokens carry `sid`.
- Tables: `identity_oidc_applications`, `_authorizations`, `_scopes`, `_tokens` (Module's `002-oidc`) and
  `identity_data_protection_keys` (`003-spine`); this package only reads and writes them.
- Boot fails closed on unusable keys, on ephemeral keys outside a local run, on an invalid client registry, and on a
  missing or unfit ring certificate outside a local run.

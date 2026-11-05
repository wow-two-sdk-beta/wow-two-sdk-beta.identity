# Tokens

`FakeTokenIssuer`: signs person, service and exchanged access tokens with an in-memory key, in the shape Identity issues (`product/features/service-tokens.md` § Tokens), so a consumer tests with no Identity running. Usage: the package `README.md`.

- `AddFakeTokenIssuer` post-configures the default `JwtBearer` scheme to trust only the fake and keeps the host's audiences. It
  replaces the issuer check, so it runs after `AddJwtBearerAuthentication` read its settings and a test of the audience or the
  scopes needs nothing else. It tests nothing about the host's real issuer.
- `AsTrustedIssuer` lists the fake in `JwtOptions.TrustedIssuers`, for a test of the real trusted-issuer pipeline: the issuer
  check, the partition and the audiences all stay. For the host's real discovery, serve a loopback JWKS through
  `JwtOptions.BackchannelHttpHandler`.
- `CreateTokenEndpointHandler` answers `/connect/token` for a caller under test: `client_credentials` and token exchange, with Identity's error codes and no client registry.
- Identity's shape is the contract here: a change to `OidcClaimMapper` or `OidcServiceTokenGrants` changes this fake too.

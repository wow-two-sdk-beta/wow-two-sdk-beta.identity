# Tokens

`FakeTokenIssuer`: signs person, service and exchanged access tokens with an in-memory key, in the shape Identity issues (`product/features/service-tokens.md` § Tokens), so a consumer tests with no Identity running. Usage: the package `README.md`.

- `AddFakeTokenIssuer` post-configures the default `JwtBearer` scheme to trust only the fake and keeps the host's audiences.
- `AsTrustedIssuer` lists the fake in `JwtOptions.TrustedIssuers`, for a test of the real trusted-issuer pipeline.
- `CreateTokenEndpointHandler` answers `/connect/token` for a caller under test: `client_credentials` and token exchange, with Identity's error codes and no client registry.
- Identity's shape is the contract here: a change to `OidcClaimMapper` or `OidcServiceTokenGrants` changes this fake too.

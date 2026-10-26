# Sessions and tokens

*Last updated: 2026-10-26*

> How apps get and keep a signed-in person, and how that ends.

- OIDC authorization code + PKCE for every app; short access tokens; rotating refresh tokens (exist) (P1).
- per-app audience; first-party apps skip consent (P1); a consent screen for third parties (P3).
- signing keys rotate with an overlap in JWKS; keys stored per the secrets convention (P1).
- single sign-out across apps: back-channel logout (P1); front-channel logout may drop (Q10).
- durable session and OTP stores in PostgreSQL, safe across instances (P1).
- browser apps sign in as a backend-for-frontend; single-page apps never hold tokens (Q7, open).
- API resources verify tokens offline against cached JWKS; no introspection in P1 (Q9, open).

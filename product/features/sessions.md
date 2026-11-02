# Sessions and tokens

*Last updated: 2026-11-02*

> How apps get and keep a signed-in person, and how that ends.

- OIDC authorization code + PKCE for every app; short access tokens; rotating refresh tokens (exist) (P1).
- per-app audience; first-party apps skip consent (P1); a consent screen for third parties (P3).
- signing keys rotate with an overlap in JWKS; keys stored per the secrets convention (P1).
- single sign-out across apps: back-channel sign-out (P1); front-channel sign-out may drop (Q10).
- durable session and OTP stores in PostgreSQL, safe across instances (P1, exist).
- session lifetimes: `Fixed` (a default, and an extended one when the person asks to stay signed in) or `Sliding` (one
  lifetime after the last use); the cookie, every app's refresh chain, each BFF session and a bearer refresh family
  end with the session (exists 2026-11-02).
- browser apps sign in as a backend-for-frontend; single-page apps never hold tokens (Q7; the `Client` BFF exists).
- API resources verify tokens offline against cached JWKS; no introspection in P1 (Q9, open).

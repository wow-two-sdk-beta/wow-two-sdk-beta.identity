# Admin console and dashboards

*Last updated: 2026-10-27*

> The operator surface of an instance: who signs in, how, and what an admin may change.

- overview: sign-ins by day, method and app; failure rate; lockouts; code delivery rate and Gateway cost; active
  sessions; MFA adoption (P1 counts, P2 charts).
- sign-in log: every attempt with time, member, masked phone or email, method, app, result and reason, IP, country,
  device; filters; CSV export (P1).
- members: add, invite links (role, expiry, use limit; list, revoke), role, app access, disable, remove, sign out everywhere, unlink Telegram, reset or
  generate a temporary password, the member's sessions and sign-ins (P1 without passwords).
- sessions: every live session; revoke one, a member's, or all (P1).
- apps: client registry with redirect URIs, allowed methods, token lifetimes and theme (P1 by config, P2 in UI).
- policies: password, code, lockout, session lifetime, required MFA (P2).
- security events and admin actions on an append-only trail (P1 table, P2 chain; Q24 open).
- impersonation with a visible banner and an audit row (P3).
- operations: OpenTelemetry meters and `/health` from P1 (decided 2026-10-25).
- layout: the Wheelhouse shell for now, sections in the sidebar menu and page actions in the top bar; reviewed and
  refined once the wow2 console runs (decided 2026-10-27).
- the management screens ship in `@wow-two-beta/fe-identity`'s `./admin` export; `apps/web` composes them into the
  console, and a central services console or a product's admin page imports the same export (decided 2026-10-26).

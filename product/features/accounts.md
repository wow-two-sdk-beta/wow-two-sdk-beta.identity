# Accounts and members

*Last updated: 2026-11-02*

> People, organizations and memberships inside one instance.

- invite-only mode per client: an admin adds a member by phone or email with a role, or hands over an invite link;
  self sign-up stays off (P1). Exists 2026-11-02 instance-wide as `Identity:SignUp:Mode=Invite`, the default.
- invite links (P1, decided 2026-10-27): each link carries a role and app access; its expiry (a date or none) and its
  use limit (one, a number, or none) are set per link. A single-use link with no date closes once someone joins
  through it; an open link with neither takes everyone until revoked. Admins list and revoke links; the wow2 instance
  uses expiring single-use links. Exists 2026-11-02: single-use, 7-day links with a fixed role and an optional bound
  phone or email, listed and revoked through `/api/organizations/{id}/invitations`; use limits beyond one and app
  access wait.
- self sign-up mode with email or phone verification for public products (P2); `Mode=Open` exists, its guest with no
  organization role and its email-first form wait for C13.
- one person per instance (D3); organizations, memberships, roles and per-app permissions as token claims; Ocharo's
  `owner`, `editor`, `viewer` carry over with `can-edit` and `can-approve` (P1). The role catalog is fixed in code:
  `owner`, `admin`, `editor`, `viewer`, `billing`; settings add permissions only (C3, exists 2026-11-02).
- one active organization per token, switched on demand, not every membership (Q8, open).
- several phone numbers per person, one primary, all in one table (P1, decided 2026-10-26).
- account linking: phone, Telegram, Google and passkeys on one person (P2); merging two accounts is open (Q23).
- profile: name, avatar, locale (`uz`, `ru`, `en`), timezone (P1 name and locale).
- disable, remove, personal-data export and deletion (P1 disable and remove; export exists).
- extension: products keep their own per-person data in their own tables keyed by `sub`; claims contributors add
  token claims; a small `attributes` document on users and organizations is the narrow exception (decided 2026-10-26).

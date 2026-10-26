# Passwords

*Last updated: 2026-10-26*

> Password storage, policy and lifecycle; every policy is configuration, off by default.

- Argon2id with rehash on upgrade (exists); breached-password check by k-anonymity (exists, fails open).
- a zxcvbn-style strength score and meter in the frontend (P2).
- policy per client: minimum length, composition rules, history of the last N, expiry after N days (P2).
- every policy is a setting, all off by default; the minimum length defaults to 15, per NIST SP 800-63B
  (decided 2026-10-25).
- forced reset by an admin or on a detected breach; a notification on every change (P2).
- generation: an admin-issued temporary password that must change at first sign-in; a generator in the password
  field with length, character sets and passphrase mode (P2).
- lockout with progressive delay per account and per IP (fixed lockout exists; progressive in P2).

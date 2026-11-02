# features/

*Last updated: 2026-11-03*

> Identity's feature groups, one spec each. Phases: P1 the Ocharo slice, P2 the whole vector, P3 on a named consumer.
> State per group: `engineering/planning/backlog.md` § Features.

| Group | Spec | P1 holds |
|---|---|---|
| Sign-in methods | [sign-in-methods.md](sign-in-methods.md) | phone + code via own Telegram bot; Sign in with Telegram |
| Accounts and members | [accounts.md](accounts.md) | invite-only members, organizations, roles, disable and remove |
| Passwords | [passwords.md](passwords.md) | — (Argon2id and the breached check exist) |
| Second factors and step-up | [second-factors.md](second-factors.md) | passkey or TOTP for console operators |
| Sessions and tokens | [sessions.md](sessions.md) | code + PKCE, per-app audience, key rotation, back-channel sign-out |
| Abuse protection | [abuse-protection.md](abuse-protection.md) | code rules, rate limits, the bot's contact check |
| Admin console | [admin-console.md](admin-console.md) | members, sessions, sign-in log, counts |
| Member self-service | [self-service.md](self-service.md) | sessions and recent sign-ins |
| Trusted contacts | [trusted-contacts.md](trusted-contacts.md) | the inviter receives an invitee's codes; one-click revoke; bot message cleanup |
| Developer surface | [developer-surface.md](developer-surface.md) | the OIDC client, phone + code screens, test kit, `admin create`, claims hook |

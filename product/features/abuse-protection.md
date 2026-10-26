# Abuse protection

*Last updated: 2026-10-26*

> Limits that keep codes, channels and accounts from being guessed, pumped or hijacked.

- codes: 6 digits, 5-minute life, 5 attempts, 30-second resend, hashed at rest, one live code per phone (P1).
- the failure count survives a regenerated code, and a code is bound to its sign-in request (Q19, open).
- rate limits per phone, per IP and per client (P1); a country allow-list against SMS pumping, with a daily spend
  cap per paid channel (P2).
- the bot accepts only the sender's own contact (`contact.user_id` equals the sender) (P1).
- hosted pages refuse framing (`frame-ancestors 'none'`) and load no third-party assets (Q19, open).
- a new-device alert through Telegram or email (P2); impossible-travel and risk scoring (P3).

# Identity.Core.PhoneSignIn

Sign in by phone with a one-time code the instance's own Telegram bot sends. A number the bot already reaches gets its
code at once; any other number gets a start link, and the code follows once the person shares that number as their own
contact in the bot.

## Quick start

```csharp
identity.AddDatabaseOtpStore()                 // codes in the database, shared by every instance (one Identity:Otp:HashKey)
        .AddPhoneSignIn();                     // phone slice + the bot's links + UserPhoneSignInService

app.MapGroup("/api/account").MapPhoneSignInEndpoints();     // phone-sign-in/send · phone-sign-in
app.MapTelegramWebhook();                                    // POST /api/telegram/webhook
```

```jsonc
"Identity": {
  "Telegram": { "IsEnabled": true, "BotToken": "…", "BotUserName": "…", "WebhookSecret": "…", "UpdateMode": "Webhook" },   // vault secrets; Polling on a local run
  "Phone": { "AllowedCountries": ["UZ"] },
  "Otp": { "HashKey": "…" }                    // base64 or base64url, 32+ bytes, a vault secret; required on a deployed boot while the bot is on
}
```

| Route | Does |
|---|---|
| `POST phone-sign-in/send` | `{ phone }` → `{ isCodeSent, telegramLinkUrl }`: `isCodeSent` when the code went to the linked chat, else the link to open |
| `POST phone-sign-in[?useCookies=true&clientId=]` | `{ phone, code }`: a cookie (`204`) or a bearer token pair, as `sign-in` answers |

With trusted contacts registered (`AddTrustedContacts`, `../TrustedContacts/trusted-contacts.md`): `phone-sign-in/targets`
lists the number's contacts, `send` takes a `targetId` that sends the code to one of them, `phone-sign-in` takes such a
code too (method `trusted_contact_code`) and answers a contact's own number with `accountChoice`, and
`phone-sign-in/choose` signs in as the account picked.

The account's own code on a browser it hasn't used waits for a contact's press: both routes answer `200` with
`deviceConfirmation` and no session, and `phone-sign-in/confirmation` signs in once approved
(`../TrustedContacts/trusted-contacts.md` § New devices).

## Notes

- Both routes answer `404` (`PhoneSignInUnavailable`) while `Identity:Telegram:IsEnabled` is off; the bot, its webhook
  and the routes are registered either way.
- Whether an account holds a number changes neither answer of `send`, but a number linked to the bot gets its code
  (`isCodeSent` true) where any other gets a start link, so `send` tells linked numbers apart; `400` `InvalidPhoneNumber`
  or `PhoneCountryNotAllowed`, `429` `PhoneCodeRateLimited` when a code or link went out within 30 s for the number
  (`Identity:Otp:RateLimitWindow`) or 5 s from the address (`Identity:Otp:OriginRateLimitWindow`), or the number spent its
  failure budget.
- Codes are single-use, limited to `Identity:Otp:MaxAttempts` wrong tries each and to `Identity:Otp:MaxSubjectFailures`
  (10) wrong tries per number across all its codes within `Identity:Otp:SubjectFailureWindow` (1 h), and stored only as
  keyed digests (`identity_otp_codes`); a wrong, spent, superseded or expired code is `401` `InvalidPhoneCode`. A new code
  retires the older ones, and a code the chat refused is discarded.
- A locked-out person answers `429` `LockedOut` before the code is checked, so the code stays unspent.
- Both routes take the per-address `identity-credentials` rate-limit policy (`../Endpoints/endpoints.md`).
- A verified code signs in with `amr` `otp` and sign-in log method `phone_code`; lockout and preconditions still apply,
  and a second factor answers `401` with the ticket for `sign-in/two-factor`.
- A first sign-in creates the person, holding the number confirmed, only while `Identity:SignUp:Mode` is `Open` or the
  request carries a pending invitation's `invitationToken`; else `403` (`SignUpClosed`) and the refusal is logged. With
  a token the person, new or known, joins its organization with its role; one naming another number answers `403`
  (`InvitationIdentifierMismatch`) before the code is spent.
- A bearer sign-in on a cookie-only host answers `400` (`BearerSignInUnsupported`) before the code is spent.
- A hosted instance reads the bot by webhook and registers it with Telegram at start; a local run sets
  `Identity:Telegram:UpdateMode` to `Polling` with a dev bot of its own; the runtime has no fake bot. Update modes, registration and the `409` a hosted bot answers to polling: `Module/Telegram/telegram.md`.
- The bot links a number to a chat only from the sender's own shared contact; the link lives in `identity_telegram_links`,
  unique per bot user name and number, so replacing the bot starts with no links. Bot options, wording and the webhook:
  `Module/Telegram/`.
- Schema: migration set `004-phone-sign-in` (`identity_otp_codes`, `identity_telegram_links`,
  `identity_telegram_link_requests`), and `008-telegram-link-changes` (`identity_telegram_link_changes`); none of them is
  published to products.
- A number a person holds confirmed and linked to another Telegram account does not move to a new one: the share waits
  in `identity_telegram_link_changes` for an owner or admin of the person's organization to approve it.
- A code the number's own chat did not receive may be one a trusted contact received for the person: `phone-sign-in`
  tries the person's deliverable contacts once the number's own code fails, unless that code ran out of tries or the
  number spent its failure budget.

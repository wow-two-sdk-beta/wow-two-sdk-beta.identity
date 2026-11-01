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
  "Telegram": { "IsEnabled": true, "BotToken": "…", "BotUserName": "…", "WebhookSecret": "…" },   // vault secrets
  "Phone": { "AllowedCountries": ["UZ"] },
  "Otp": { "HashKey": "…" }                    // base64, 32+ bytes, a vault secret; required on a deployed boot while the bot is on
}
```

| Route | Does |
|---|---|
| `POST phone-sign-in/send` | `{ phone }` → `{ isCodeSent, telegramLinkUrl }`: `isCodeSent` when the code went to the linked chat, else the link to open |
| `POST phone-sign-in[?useCookies=true&clientId=]` | `{ phone, code }`: a cookie (`204`) or a bearer token pair, as `login` answers |

## Notes

- Both routes answer `404` (`PhoneSignInUnavailable`) while `Identity:Telegram:IsEnabled` is off; the bot, its webhook
  and the routes are registered either way.
- Known and unknown numbers answer alike; `400` `InvalidPhoneNumber` or `PhoneCountryNotAllowed`, `429`
  `PhoneCodeRateLimited` when a code or link went out moments ago for the number or the address (`Identity:Otp:RateLimitWindow`).
- Codes are single-use, limited to `Identity:Otp:MaxAttempts` wrong tries, and stored only as keyed digests
  (`identity_otp_codes`); a wrong, spent or expired code is `401` `InvalidPhoneCode`.
- A verified code signs in with `amr` `otp` and sign-in log method `phone_code`; lockout and preconditions still apply,
  and a second factor answers `401` with the ticket for `login/two-factor`.
- A first sign-in creates the person, holding the number confirmed, only while `Identity:SignUp:IsEnabled` is on; else `403`
  (`SignUpClosed`) and the refusal is logged.
- A bearer sign-in on a cookie-only host answers `400` (`BearerSignInUnsupported`) before the code is spent.
- The bot links a number to a chat only from the sender's own shared contact; the link lives in `identity_telegram_links`,
  unique per bot user name and number, so replacing the bot starts with no links. Bot options, wording and the webhook:
  `Module/Telegram/`.
- Schema: migration set `004-phone-sign-in` (`identity_otp_codes`, `identity_telegram_links`,
  `identity_telegram_link_requests`); none of them is published to products.

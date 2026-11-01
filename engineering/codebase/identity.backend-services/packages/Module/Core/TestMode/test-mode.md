# Identity.Core.TestMode

A local run with no outside effects: every code and link the instance sends, by email or by Telegram, stays in memory and is
readable over HTTP, and phone sign-in completes its Telegram link without a real bot. For tests and for working on a
product against its own instance; a deployed boot refuses it.

## Quick start

```csharp
identity.AddPhoneSignIn().AddTestMode();      // after the email broker and AddPhoneSignIn, which it wraps
app.MapTestModeEndpoints();                    // GET /api/test/messages?to=
```

```jsonc
"Identity": { "TestMode": { "IsEnabled": true }, "Phone": { "AllowedCountries": ["UZ"] } }   // appsettings.Local.json or an env var
```

```bash
curl -X POST   http://127.0.0.1:8080/api/account/phone-sign-in/send -H 'content-type: application/json' -d '{"phone":"+998901234567"}'
curl           'http://127.0.0.1:8080/api/test/messages?to=%2B998901234567'
```

| Route | Does |
|---|---|
| `GET /api/test/messages[?to=]` | anonymous; `{ data: [{ channel, to, subject, text, sentAt }] }`, oldest first; `404` while test mode is off |

- `to` is an email address (case-insensitive), a phone number (the messages in the Telegram chat it is linked to) or a chat
  id; without it every kept message returns. The newest 1000 are kept; a restart empties the sink.

## What changes while it is on

- `ITelegramBotClient` is a fake that keeps each message the bot sends; nothing reaches Telegram, so no bot token is needed.
- `IEmailBroker` keeps each message per recipient and reports success; nothing is sent, even with an SMTP relay configured.
- `Identity:Telegram` is switched on in `Webhook` mode with placeholders for the token, user name and webhook secret the host
  left out (a configured value stays), so phone sign-in runs, nothing polls, and the real webhook stays shut by a secret
  nobody knows.
- `IPhoneSignInLinkSimulator` plays the person at a new start link: it feeds the bot's own `TelegramUpdateHandler` the
  `/start` message and the shared contact, so the link, its checks and the code that follows run the real path. The person's chat
  id is the digits of the number, so `send` answers `isCodeSent` at once and the bot's replies, the code last, are in the sink.
- The OTP rate limit still applies; `Identity:Otp:RateLimitWindow` `00:00:00` lets a test ask again at once.

## Notes

- `Identity:TestMode:IsEnabled` is read at start and a deployed boot (anything but Development without `DEPLOY_ENVIRONMENT`)
  refuses it, because the route hands every code to any caller. With no host environment to judge by, the boot counts as deployed.
- Off, nothing changes: the earlier client and broker registrations stay, the route answers `404` and the simulator answers false.
- A person who signs in this way is real: the row is in the database, with the number confirmed.

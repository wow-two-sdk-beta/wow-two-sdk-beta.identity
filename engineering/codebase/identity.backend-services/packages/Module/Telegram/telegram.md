# Identity.Telegram

The instance's own Telegram bot: it links a phone number to a chat through its start link and the person's shared contact,
sends the chat its codes and links, and reads the bot's updates by webhook or by polling. Sign-in by phone is built on it
(`Core/PhoneSignIn/`); the delivery channel is `Otp/Telegram/`.

## Quick start

```csharp
identity.AddTelegramLinks();                  // or .AddPhoneSignIn(), which includes it
app.MapTelegramWebhook();                     // POST /api/telegram/webhook
```

```jsonc
"Identity": {
  "Telegram": {
    "IsEnabled": true,
    "BotToken": "…",                          // {bot id}:{secret}; a vault secret, never in a repo, log or message
    "BotUserName": "ocharo_bot",              // start links read https://t.me/{BotUserName}?start={token}
    "WebhookSecret": "…",                     // 1–256 of A-Z a-z 0-9 _ -; a vault secret; required in Webhook mode
    "UpdateMode": "Webhook",                  // Webhook (default) | Polling; read at start
    "WebhookUrl": "https://id.example/api/telegram/webhook",   // optional: the host fills it from Identity:Server:Issuer
    "LinkLifetime": "00:15:00"
  }
}
```

## Update modes

| Mode | Where | Does |
|---|---|---|
| `Webhook` | every hosted instance | Telegram posts each update to `POST /api/telegram/webhook` with the secret header; a deployed boot registers the address itself |
| `Polling` | a local run only | `TelegramPollingService` long-polls `getUpdates` (`message` only) and hands each update to the same `TelegramUpdateHandler` |

- A local run is Development without `DEPLOY_ENVIRONMENT`, the test the OIDC key refusal uses. A deployed boot refuses
  `Polling` at start, and the webhook answers `404` while the mode is `Polling`.
- Webhook registration (`TelegramWebhookRegistrationService`) runs once, in the background, on a deployed boot with the bot
  on: it reads `getWebhookInfo` and calls `setWebhook` (address, `secret_token`, allowed updates `["message"]`) only when
  the address or the allowed updates differ, or Telegram's last delivery was refused with `401`. A failure is logged and
  the start goes on. The address is `{origin of Identity:Server:Issuer}/api/telegram/webhook` unless
  `Identity:Telegram:WebhookUrl` names another; with none, nothing is registered.
- Telegram never returns the `secret_token`, so a rotated `WebhookSecret` shows only as the `401` Telegram records for the
  rejected deliveries; the next start re-registers. After rotating, restart once more or call `setWebhook` by hand.
- Polling never calls `deleteWebhook`. A bot with a webhook set belongs to a hosted instance, so Telegram answers `409`:
  the service logs one error (once per episode) that says to give the local run its own dev bot, then retries with a doubling wait, 5 s up to 1 min. Another poller on the same bot is reported the same way.
- An update whose handler throws is logged and skipped under polling, so one poisoned update cannot stop the bot; the
  webhook answers `500` and lets Telegram retry. Updates are confirmed by `getUpdates` offset, after they are handled.

## Notes

- The bot is the person's way in, so a number links to a chat only from the sender's own shared contact; a forwarded contact
  card or another number is refused. The link lives in `identity_telegram_links`, unique per bot user name and number, so
  replacing the bot starts with no links.
- A linked number never moves on its own to another Telegram account while a person holds it confirmed: carriers recycle
  numbers, and the new holder can register a new Telegram account with the old number. Telegram's user id
  (`telegram_user_id`) is stable per account, so no device binding is needed:

  | Shared contact | Does |
  |---|---|
  | number with no link | links, and the start link's purpose completes |
  | number linked to the same Telegram user | relinks, and the purpose completes |
  | number linked to another Telegram user, nobody holds it confirmed | moves the link, and the purpose completes |
  | number linked to another Telegram user, a person holds it confirmed | the link stays; the start link is spent with no code and no sign-up; a pending change is recorded and the bot answers `link-change-pending` |

- A pending change lives in `identity_telegram_link_changes` (`008-telegram-link-changes`): one row per bot and number with
  the asking chat, its Telegram user and the request time; a later request replaces it. It counts for a day
  (`TelegramLinkChangeService.Lifetime`) and is pruned as new ones arrive. An owner or admin of an organization the person
  belongs to approves it (the link moves to the asking chat; nothing else rotates) or denies it (the row goes) through
  `MapOrganizationEndpoints` (`../Core/Organizations/organizations.md`); account deletion drops it with the links. A person
  in no organization has no one to approve it.
- `TelegramVerifiedChannelSource` lists the chat a person's confirmed phone is linked to (on the current bot) as a
  `telegram` verified channel, after the email source, so password-change codes and links reach Telegram when there is no
  confirmed email or the email fails. It lists nothing while the bot is off.
- A start link is `https://t.me/{BotUserName}?start={token}` with 32 random characters; only the token's SHA-256 is stored,
  and the link expires after `LinkLifetime`. Making one is rate-limited per number by `Identity:Otp:RateLimitWindow` (30 s) and per origin by
  `Identity:Otp:OriginRateLimitWindow` (5 s).
- The bot's own texts (welcome, share prompt, mismatch, expired, pending link change) come in `en`, `ru` and `uz`;
  `Identity:Telegram:Messages:{culture}:{key}` overrides them.
- Boot check, failing closed while the bot is on: a token of `{bot id}:{secret}`, a user name Telegram allows, a known mode,
  the secret in Webhook mode, an absolute HTTPS `WebhookUrl` when set, a positive lifetime, and on a deployed boot
  `Identity:Otp:HashKey`.
- The two background services are registered either way and act only in their own mode; an instance with the bot off needs no token.

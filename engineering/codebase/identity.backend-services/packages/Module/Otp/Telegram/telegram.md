# Identity.Otp.Telegram

Telegram delivery channel for `Identity/Otp`. `OtpDeliveryEnvelopeModel.DeliveryAddress` must be the **numeric chat id**
of a chat the bot may write to. The instance's own bot captures it: `/start` and Share Contact link a phone number to a
chat (`Module/Telegram/`, used by sign-in by phone), so a host that runs `AddTelegramLinks()` or `AddPhoneSignIn()` needs
no link flow of its own, and a person's linked chat is also a verified channel for password-change codes and links.

```csharp
identity.AddPhoneSignIn();                    // the bot, its links, the telegram channel, sign-in by phone

// Alone, for a host with its own bot and chat ids:
builder.Services
    .AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(config["Auth:BotToken"]!))
    .AddOtpService()
    .AddTelegramOtpDelivery(o =>
    {
        o.ScopeDisplayNames["crm"]   = "Haven CRM";
        o.ScopeDisplayNames["admin"] = "Haven Admin";
    });
```

- `AddTelegramLinks()` registers the client from `Identity:Telegram:BotToken`; the SDK never takes a token any other way
  (vault secret, never in a repo, log or message). A client the host registers first wins.
- The envelope's `Text` is sent as is; without it the fallback template applies: `{0}` scope display name · `{1}` code ·
  `{2}` lifetime minutes. Options also bind from `Identity:Otp:Telegram`.
- Failures return `OtpDeliveryResult(false, reason)` — `invalid_chat_id` for malformed addresses,
  Telegram API errors pass through as the reason message (a blocked bot is a `403`).
- Registered keyed as the `telegram` channel and additively (`TryAddEnumerable`) beside the other channels.
- The runtime always talks to the real Bot API; tests swap `ITelegramBotClient` for a fake of their own.
- Codes to a phone number without a bot chat: the `telegram-gateway` channel (`Otp/TelegramGateway`).

# Identity.Notices

App notices in process: an app's message to one person through the instance's own Telegram bot
(`../Telegram/telegram.md`), to the chat the person's confirmed phone is linked to. The contract, the HTTP route and the
answers: [contract](../../../../../architecture/contract.md#app-notices).

## Quick start

```csharp
identity.AddAppNotices(o => o.LocalClientId = "desk");       // adds AddTelegramLinks when missing

var result = await notices.SendNoticeAsync(new AppNotice     // IAppNoticeBroker, scoped
{
    Subject = person.Id.ToString(), Text = "<b>Fashion week</b> opens Monday", IdempotencyKey = $"digest-42:{person.Id}",
    Buttons = [new AppNoticeButton { Text = "Open", Url = "https://events.example/e/42" }], Culture = "uz-Latn",
});
```

## How a notice goes

| Step | Does |
|---|---|
| rules | `AppNoticeService.FindProblem`: the `sub`, the text and its HTML (`TelegramHtml`), the buttons, the key, the culture → `NoticeInvalid` |
| bot | off → `NoticeUnavailable` |
| app's rate limit | `AppNoticePace.TryAdmit`, sliding, per app → `NoticeRateLimited` |
| key | `IAppNoticeRepository.ClaimAsync` on `identity_app_notices` (`013-app-notices`): a repeat answers the first outcome, another body `IdempotencyKeyReused`, a send under way `NoticeInProgress`; a pending row older than a minute is taken over |
| link | the person's confirmed phone → the bot's link; none → `not_linked`, the key given back |
| mute | `identity_notice_mutes`, the app's row or `*` → `muted`, kept under the key |
| pace | `AppNoticePace.WaitTurnAsync`: 25 a second for the bot, one a second per chat, up to `MaxPaceWait` → `NoticeRateLimited` |
| send | `ITelegramNoticeSender` with `IsHtml`, `IsKept`, the app's links and a mute button (`app-notices.mute`, the payload the app's client id, pressable only by the person's Telegram user) → `sent` with the ledger id; Telegram's `429` → `NoticeRateLimited`, an outage → `NoticeUnavailable`, any other refusal → `NoticeRefused`, the key given back |

## Mutes

- The mute button (`AppNoticeMuteHandler`) mutes the notice's app for the person and answers a toast naming `/start`.
- `/stop` to the bot mutes every app (`*`) for each person holding confirmed a number the sender linked; a bare `/start`
  lifts their mutes and says so (`notices-resumed`), else it answers the welcome.
- Codes and the bot's own notices (trusted contacts, new devices) go through their own paths and never read a mute.
- The store, the button handler, `/stop` and the account deletion handler come with `AddTelegramLinks`, so a mute set on
  any instance holds even where the instance sends no notices.

## Notes

- An app notice is kept: its ledger row has no `delete_at`, so the sweep never deletes it, and it replaces no earlier one.
  Deleting the account drops the rows (`ITelegramMessageLedger.RemoveKeptAsync`), its keys and mutes; the messages stay.
- The pace and the rate limit live in one process: replicas each keep their own.
- Settings: `Identity:Notices` (`AppNoticeOptions`); the service's route adds `Identity:Notices:Audience`.

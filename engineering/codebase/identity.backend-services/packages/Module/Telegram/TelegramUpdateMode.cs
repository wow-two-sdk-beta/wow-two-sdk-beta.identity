namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>Names how the instance receives the bot's updates.</summary>
public enum TelegramUpdateMode
{
    /// <summary>Telegram posts each update to the instance's webhook; for every hosted instance.</summary>
    Webhook = 0,

    /// <summary>The instance long-polls Telegram's <c>getUpdates</c>; for a local run, which has no public address.</summary>
    Polling = 1,
}

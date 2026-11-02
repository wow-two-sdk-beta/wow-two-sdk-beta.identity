namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>A recorded inline button and the message it is on, as a press finds them.</summary>
/// <param name="Callback">The button.</param>
/// <param name="Message">The message it is on.</param>
public sealed record TelegramCallbackRecord(IdentityTelegramCallback Callback, IdentityTelegramMessage Message);

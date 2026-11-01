namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>Names what a completed start link leads to; each purpose has one <see cref="ITelegramLinkCompletionHandler"/>.</summary>
public static class TelegramLinkPurposeConstants
{
    /// <summary>Sign in by phone: the bot sends the pending sign-in code once the number is linked.</summary>
    public const string PhoneSignIn = "phone-sign-in";
}

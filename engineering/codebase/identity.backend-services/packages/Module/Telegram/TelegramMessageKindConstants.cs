namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>Names the kinds of message the bot records itself; a notice's sender names its own kinds.</summary>
public static class TelegramMessageKindConstants
{
    /// <summary>A one-time code the <c>telegram</c> OTP channel sent; it is cleaned up once the code expires.</summary>
    public const string Code = "code";
}

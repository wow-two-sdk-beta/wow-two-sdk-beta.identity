namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>Names the kinds of message the bot records itself; a notice's sender names its own kinds.</summary>
public static class TelegramMessageKindConstants
{
    /// <summary>A one-time code the <c>telegram</c> OTP channel sent; it is cleaned up once the code expires.</summary>
    public const string Code = "code";

    /// <summary>The notice to an account's own chat after a sign-in through one of its trusted contacts, with its revoke button.</summary>
    public const string ContactSignIn = "contact-sign-in";

    /// <summary>The notice asking a trusted contact to answer for an account's new device, with its two buttons.</summary>
    public const string DeviceConfirmation = "device-confirmation";
}

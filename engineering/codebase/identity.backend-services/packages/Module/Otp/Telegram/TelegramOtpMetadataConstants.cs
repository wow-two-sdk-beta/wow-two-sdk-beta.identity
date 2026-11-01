namespace WoW.Two.Sdk.Beta.Be.Identity.Otp.Telegram;

/// <summary>Names the <c>OtpDeliveryEnvelopeModel.Metadata</c> keys the Telegram channel reads.</summary>
public static class TelegramOtpMetadataConstants
{
    /// <summary>Present (any value), the code message also removes the reply keyboard the bot showed, such as a share-contact button.</summary>
    public const string RemoveKeyboard = "telegram.remove-keyboard";
}

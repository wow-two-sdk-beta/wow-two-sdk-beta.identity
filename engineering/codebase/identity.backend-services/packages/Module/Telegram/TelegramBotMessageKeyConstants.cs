namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>Names the texts the bot sends besides codes; <see cref="TelegramBotOptions.Messages"/> overrides them per culture.</summary>
public static class TelegramBotMessageKeyConstants
{
    /// <summary>Asks the person to share their number with the button, after a valid start link.</summary>
    public const string SharePrompt = "share-prompt";

    /// <summary>The share-contact button's label.</summary>
    public const string ShareButton = "share-button";

    /// <summary>A shared contact belongs to someone else.</summary>
    public const string NotOwnContact = "not-own-contact";

    /// <summary>The shared number differs from the one the link was made for.</summary>
    public const string NumberMismatch = "number-mismatch";

    /// <summary>The link is unknown, spent or expired, or no link is pending.</summary>
    public const string LinkExpired = "link-expired";

    /// <summary>A plain <c>/start</c> without a link token.</summary>
    public const string Welcome = "welcome";

    /// <summary>A code went out moments ago, so none followed the shared contact.</summary>
    public const string CodeRecentlySent = "code-recently-sent";
}

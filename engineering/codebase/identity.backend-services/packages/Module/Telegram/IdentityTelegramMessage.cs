namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>
/// One message the instance's bot sent and must clean up (<c>identity_telegram_messages</c>): a code or a notice. Once
/// <see cref="DeleteAt"/> passes, the minute sweep (<see cref="TelegramMessageSweeper"/>) deletes the message when it was
/// sent under 48 hours ago, as Telegram allows, or edits it to a one-line stub with no buttons, then drops the row.
/// </summary>
public sealed class IdentityTelegramMessage
{
    /// <summary>Primary key, a Guid v7 made in code.</summary>
    public Guid Id { get; set; }

    /// <summary>The bot's user name, lower case, without <c>@</c>; only that bot can touch the message.</summary>
    public string BotUserName { get; set; } = string.Empty;

    /// <summary>The private chat the message is in.</summary>
    public long ChatId { get; set; }

    /// <summary>Telegram's id of the message inside <see cref="ChatId"/>.</summary>
    public int MessageId { get; set; }

    /// <summary>What the message is: <see cref="TelegramMessageKindConstants.Code"/>, or a notice kind its sender names.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The account the message is about, for notices; null for codes.</summary>
    public Guid? UserId { get; set; }

    /// <summary>The culture the stub is worded in, such as <c>uz</c>; null takes the bot's default culture.</summary>
    public string? Culture { get; set; }

    /// <summary>When the bot sent it (UTC).</summary>
    public DateTimeOffset SentAt { get; set; }

    /// <summary>When the sweep cleans it up (UTC): a code's expiry, a notice's lifetime, or at once once superseded.</summary>
    public DateTimeOffset DeleteAt { get; set; }
}

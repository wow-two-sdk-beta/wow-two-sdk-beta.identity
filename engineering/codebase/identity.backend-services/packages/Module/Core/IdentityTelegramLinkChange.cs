namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>
/// One pending change of a phone number's Telegram link (<c>identity_telegram_link_changes</c>): another Telegram account
/// shared a number a person holds confirmed, while the number stays linked to the account it was linked from. An owner
/// or admin of an organization the person belongs to approves it, which moves the link to this chat, or denies it. One
/// row per bot and number, the latest request replacing an earlier one; rows a day old no longer count and are pruned as
/// new ones arrive.
/// </summary>
public sealed class IdentityTelegramLinkChange
{
    /// <summary>Primary key, a Guid v7 made in code.</summary>
    public Guid Id { get; set; }

    /// <summary>The bot's user name, lower case, without <c>@</c>.</summary>
    public string BotUserName { get; set; } = string.Empty;

    /// <summary>The number in E.164.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>The private chat that asked for the link.</summary>
    public long ChatId { get; set; }

    /// <summary>The Telegram user who shared the number as their own contact.</summary>
    public long TelegramUserId { get; set; }

    /// <summary>When the contact was shared (UTC).</summary>
    public DateTimeOffset RequestedAt { get; set; }
}

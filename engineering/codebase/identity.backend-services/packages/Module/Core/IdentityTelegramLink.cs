namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>
/// One phone number linked to a Telegram chat of the instance's bot (<c>identity_telegram_links</c>): the person shared
/// that number as their own contact in the bot, so the bot may send its codes there. Keyed by bot and number, so a
/// replaced bot starts with no links; a number linked again from another chat moves to it.
/// </summary>
public sealed class IdentityTelegramLink
{
    /// <summary>Primary key, a Guid v7 made in code.</summary>
    public Guid Id { get; set; }

    /// <summary>The bot's user name, lower case, without <c>@</c>.</summary>
    public string BotUserName { get; set; } = string.Empty;

    /// <summary>The linked number in E.164.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>The private chat the bot sends codes to.</summary>
    public long ChatId { get; set; }

    /// <summary>The Telegram user who shared the number as their own contact.</summary>
    public long TelegramUserId { get; set; }

    /// <summary>When the number was linked to this chat (UTC).</summary>
    public DateTimeOffset LinkedAt { get; set; }
}

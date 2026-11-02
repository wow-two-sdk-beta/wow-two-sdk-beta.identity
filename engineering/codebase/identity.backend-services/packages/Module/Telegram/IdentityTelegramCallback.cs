namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>
/// One inline button on a recorded message (<c>identity_telegram_callbacks</c>). Its <c>callback_data</c> is an opaque random
/// token stored only as its SHA-256; it works once, only for <see cref="AllowedTelegramUserId"/>, and pressing one spends
/// every button of the message. The row goes with its message's row.
/// </summary>
public sealed class IdentityTelegramCallback
{
    /// <summary>Primary key, a Guid v7 made in code.</summary>
    public Guid Id { get; set; }

    /// <summary>The <see cref="IdentityTelegramMessage.Id"/> of the message the button is on.</summary>
    public Guid MessageRecordId { get; set; }

    /// <summary>The hex SHA-256 of the button's callback data.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>The action name the press is dispatched to (<see cref="ITelegramCallbackHandler"/>, keyed by it).</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>What the action acts on, such as a record id; null for none.</summary>
    public string? Payload { get; set; }

    /// <summary>The one Telegram user (<c>callback_query.from.id</c>) whose press counts.</summary>
    public long AllowedTelegramUserId { get; set; }

    /// <summary>When the button, or another of its message, was pressed (UTC); null while unused.</summary>
    public DateTimeOffset? UsedAt { get; set; }
}

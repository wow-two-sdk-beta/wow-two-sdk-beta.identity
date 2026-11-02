namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>
/// One pending start link of the instance's bot (<c>identity_telegram_link_requests</c>): <c>t.me/{bot}?start={token}</c>
/// made for a phone number, or for an invitation sign-up whose number the contact names. Opening it in Telegram attaches
/// the chat; a shared contact completes it once, holding the link's number when it names one. Only the token's SHA-256 is stored; rows past their expiry are pruned as new ones arrive.
/// </summary>
public sealed class IdentityTelegramLinkRequest
{
    /// <summary>Primary key, a Guid v7 made in code.</summary>
    public Guid Id { get; set; }

    /// <summary>The hex SHA-256 of the start token.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>What completes the link, such as <c>phone-sign-in</c>.</summary>
    public string Purpose { get; set; } = string.Empty;

    /// <summary>The bot's user name, lower case, without <c>@</c>.</summary>
    public string BotUserName { get; set; } = string.Empty;

    /// <summary>The number the link was made for, in E.164, which the shared contact must hold; null when the contact names it, as for an invitation sign-up.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>The record the purpose completes, such as the invitation sign-up; null for none.</summary>
    public Guid? ReferenceId { get; set; }

    /// <summary>The digest of the address that asked for the link, for rate limiting; null when unknown.</summary>
    public string? OriginHash { get; set; }

    /// <summary>The private chat that opened the link; null until then.</summary>
    public long? ChatId { get; set; }

    /// <summary>The Telegram user who opened the link; null until then.</summary>
    public long? TelegramUserId { get; set; }

    /// <summary>When the link was made (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the link stops working (UTC).</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>When a matching contact completed it; null while pending.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }
}

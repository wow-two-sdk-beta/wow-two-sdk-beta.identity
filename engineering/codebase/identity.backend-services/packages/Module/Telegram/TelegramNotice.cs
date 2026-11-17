namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>A notice the bot sends to a chat about an account (<see cref="ITelegramNoticeSender"/>).</summary>
public sealed record TelegramNotice
{
    /// <summary>What the notice is, at most 64 characters, such as <c>contact-sign-in</c>; a newer notice of the kind for the account replaces the older ones in the chat.</summary>
    public required string Kind { get; init; }

    /// <summary>The account the notice is about.</summary>
    public required Guid UserId { get; init; }

    /// <summary>The private chat to send it to.</summary>
    public required long ChatId { get; init; }

    /// <summary>The worded text: plain, or Telegram's HTML subset when <see cref="IsHtml"/> is set.</summary>
    public required string Text { get; init; }

    /// <summary>Whether <see cref="Text"/> is in Telegram's HTML subset (<c>parse_mode</c> <c>HTML</c>) rather than plain.</summary>
    public bool IsHtml { get; init; }

    /// <summary>The recipient's culture name, such as <c>uz</c>, for the stub the notice may become; null takes the bot's default.</summary>
    public string? Culture { get; init; }

    /// <summary>The link buttons, one per row, above <see cref="Buttons"/>; empty for none. A press of any button drops them all.</summary>
    public IReadOnlyList<TelegramNoticeLink> Links { get; init; } = [];

    /// <summary>The inline buttons, one per row; empty for none.</summary>
    public IReadOnlyList<TelegramNoticeButton> Buttons { get; init; } = [];

    /// <summary>
    /// Whether the notice stays in the chat: it is recorded with no cleanup time, so the sweep never deletes it, and it
    /// replaces no older notice of its kind. An app notice stays; the bot's own notices are cleaned up.
    /// </summary>
    public bool IsKept { get; init; }
}

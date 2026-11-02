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

    /// <summary>The worded text.</summary>
    public required string Text { get; init; }

    /// <summary>The recipient's culture name, such as <c>uz</c>, for the stub the notice may become; null takes the bot's default.</summary>
    public string? Culture { get; init; }

    /// <summary>The inline buttons, one per row; empty for none.</summary>
    public IReadOnlyList<TelegramNoticeButton> Buttons { get; init; } = [];
}

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>An inline button on a notice: one press by one Telegram user runs the action registered under <see cref="Action"/>.</summary>
public sealed record TelegramNoticeButton
{
    /// <summary>The button's label.</summary>
    public required string Label { get; init; }

    /// <summary>The action name a press is dispatched to, at most 64 characters (<see cref="ITelegramCallbackHandler"/>, keyed by it).</summary>
    public required string Action { get; init; }

    /// <summary>What the action acts on, at most 256 characters, such as a record id; null for none. It never leaves the instance.</summary>
    public string? Payload { get; init; }

    /// <summary>The one Telegram user (<c>callback_query.from.id</c>) whose press counts.</summary>
    public required long AllowedTelegramUserId { get; init; }
}

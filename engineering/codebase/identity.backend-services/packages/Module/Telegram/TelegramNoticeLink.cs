namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>A link button on a notice: a press opens <see cref="Url"/> and reaches no handler.</summary>
public sealed record TelegramNoticeLink
{
    /// <summary>The button's label.</summary>
    public required string Label { get; init; }

    /// <summary>The absolute address it opens.</summary>
    public required string Url { get; init; }
}

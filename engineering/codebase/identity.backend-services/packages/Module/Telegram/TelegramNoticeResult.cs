namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>What sending a notice came to.</summary>
public sealed record TelegramNoticeResult
{
    /// <summary>Whether Telegram took the notice.</summary>
    public bool IsSent { get; init; }

    /// <summary>Telegram's id of the sent message; null when not sent.</summary>
    public int? MessageId { get; init; }

    /// <summary>Why it was not sent: <c>bot_disabled</c>, or Telegram's refusal (a blocked bot is a <c>403</c>); null when sent.</summary>
    public string? FailureReason { get; init; }
}

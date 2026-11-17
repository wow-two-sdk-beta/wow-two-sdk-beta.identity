namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>What sending a notice came to.</summary>
public sealed record TelegramNoticeResult
{
    /// <summary>Whether Telegram took the notice.</summary>
    public bool IsSent { get; init; }

    /// <summary>Telegram's id of the sent message; null when not sent.</summary>
    public int? MessageId { get; init; }

    /// <summary>The ledger row of the sent message (<see cref="IdentityTelegramMessage.Id"/>); null when not sent.</summary>
    public Guid? RecordId { get; init; }

    /// <summary>Telegram's error code for a refusal, such as <c>403</c> for a blocked bot or <c>429</c> for flood control; null when sent, with the bot off, or when Telegram was not reached.</summary>
    public int? ErrorCode { get; init; }

    /// <summary>How long Telegram asks the bot to wait before the next try, with a <c>429</c>; null otherwise.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>Why it was not sent: <c>bot_disabled</c>, or Telegram's refusal (a blocked bot is a <c>403</c>); null when sent.</summary>
    public string? FailureReason { get; init; }
}

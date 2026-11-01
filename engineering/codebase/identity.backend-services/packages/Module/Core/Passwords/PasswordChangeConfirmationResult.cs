namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;

/// <summary>Represents the outcome of asking for a password change's confirmation.</summary>
public sealed record PasswordChangeConfirmationResult
{
    /// <summary>How this instance confirms a change.</summary>
    public required PasswordChangeConfirmationType Confirmation { get; init; }

    /// <summary>The outcome.</summary>
    public required PasswordChangeConfirmationStatus Status { get; init; }

    /// <summary>When the code or link stops working; set when <see cref="Status"/> is <see cref="PasswordChangeConfirmationStatus.Sent"/>.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>The channel's failure detail; set for <see cref="PasswordChangeConfirmationStatus.DeliveryFailed"/>.</summary>
    public string? FailureReason { get; init; }

    /// <summary>Whether a code or link went out.</summary>
    public bool IsSent => Status == PasswordChangeConfirmationStatus.Sent;
}

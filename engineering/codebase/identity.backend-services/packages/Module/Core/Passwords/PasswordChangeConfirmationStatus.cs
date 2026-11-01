namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;

/// <summary>Refers to the outcome of asking for a password change's confirmation.</summary>
public enum PasswordChangeConfirmationStatus
{
    /// <summary>The instance confirms with nothing but the current password; nothing was sent.</summary>
    NotRequired,

    /// <summary>The channel accepted the code or link.</summary>
    Sent,

    /// <summary>A code or link was sent moments ago.</summary>
    RateLimited,

    /// <summary>The person has no verified channel with a registered delivery handler.</summary>
    NoVerifiedChannel,

    /// <summary>Every channel refused the code or link.</summary>
    DeliveryFailed,
}

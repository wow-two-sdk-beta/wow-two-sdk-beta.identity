namespace WoW.Two.Sdk.Beta.Be.Identity.Core.InviteSignUp;

/// <summary>Represents what a browser sends to start signing up from an invitation.</summary>
public sealed record InviteSignUpStartRequest
{
    /// <summary>Gets the invitation link's token.</summary>
    public required string Token { get; init; }

    /// <summary>Gets the name the person gave, as typed; null when none.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Gets the uploaded picture's bytes; null when none.</summary>
    public byte[]? Picture { get; init; }

    /// <summary>Gets the app the sign-up serves, for the sign-in log; null when unknown.</summary>
    public string? ClientId { get; init; }

    /// <summary>Gets where the request came from, for rate limiting; null when unknown.</summary>
    public string? Origin { get; init; }

    /// <summary>Gets how the sign-up proves the person: their own Telegram (default) or the invitation's trusted contact.</summary>
    public InviteSignUpVerification Verification { get; init; } = InviteSignUpVerification.Telegram;
}

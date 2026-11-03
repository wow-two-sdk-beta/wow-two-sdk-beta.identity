namespace WoW.Two.Sdk.Beta.Be.Identity.Core.InviteSignUp;

/// <summary>Represents a started invitation sign-up: the bot link the person opens to verify their phone.</summary>
public sealed record InviteSignUpStartedDto
{
    /// <summary>Gets the <c>t.me</c> start link; the shared contact in the bot proves the phone. Null when the code went to the trusted contact.</summary>
    public string? TelegramLinkUrl { get; init; }

    /// <summary>Gets whether the code went to the invitation's trusted contact; the page then asks for it (<c>invitations/sign-up/verify</c>).</summary>
    public bool IsCodeSentToTrustedContact { get; init; }

    /// <summary>Gets when the sign-up stops waiting (UTC).</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}

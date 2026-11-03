namespace WoW.Two.Sdk.Beta.Be.Identity.Core.InviteSignUp;

/// <summary>Represents a started invitation sign-up: the link to open and the browser's binding, or why it did not start.</summary>
public sealed record InviteSignUpStartResult
{
    /// <summary>Gets the outcome.</summary>
    public required InviteSignUpStartStatus Status { get; init; }

    /// <summary>Gets the refusal with its code, for <see cref="InviteSignUpStartStatus.InvitationRefused"/> and <see cref="InviteSignUpStartStatus.Invalid"/>.</summary>
    public IdentityResult? Refusal { get; init; }

    /// <summary>Gets the sign-up's id, once started.</summary>
    public Guid? VerificationId { get; init; }

    /// <summary>Gets the secret the starting browser keeps to read the sign-up, shown this once.</summary>
    public string? BrowserSecret { get; init; }

    /// <summary>Gets the <c>t.me</c> start link the browser opens, once started; null when the code went to the trusted contact.</summary>
    public string? TelegramLinkUrl { get; init; }

    /// <summary>Gets whether the code went to the invitation's trusted contact, to be typed on the page (<c>invitations/sign-up/verify</c>).</summary>
    public bool IsCodeSentToTrustedContact { get; init; }

    /// <summary>Gets when the sign-up stops waiting (UTC), once started.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    internal static InviteSignUpStartResult Of(InviteSignUpStartStatus status, IdentityResult? refusal = null) => new() { Status = status, Refusal = refusal };
}

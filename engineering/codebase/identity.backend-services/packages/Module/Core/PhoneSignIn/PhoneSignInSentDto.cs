namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;

/// <summary>Answers a phone sign-in send: the code went to Telegram at once, or the start link to open first.</summary>
public sealed record PhoneSignInSentDto
{
    /// <summary>Whether the code went to the number's linked chat.</summary>
    public required bool IsCodeSent { get; init; }

    /// <summary>The <c>https://t.me/{bot}?start={token}</c> link to open when no code went out; the code follows the shared contact.</summary>
    public string? TelegramLinkUrl { get; init; }
}

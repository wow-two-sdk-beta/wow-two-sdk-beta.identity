namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;

/// <summary>The accounts a trusted contact's verified number may sign in as, and the single-use token that picks one.</summary>
public sealed record AccountChoiceDto
{
    /// <summary>The token <c>phone-sign-in/choose</c> takes; it works once, for ten minutes.</summary>
    public required string Token { get; init; }

    /// <summary>The contact's own account first, then each account trusting them.</summary>
    public required IReadOnlyList<AccountChoiceAccountDto> Accounts { get; init; }
}

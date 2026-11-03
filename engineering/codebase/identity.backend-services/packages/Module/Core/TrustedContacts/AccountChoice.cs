namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>What a trusted contact's verified code offers: a single-use token and the accounts it may sign in as.</summary>
public sealed record AccountChoice
{
    /// <summary>The token <c>phone-sign-in/choose</c> takes: single-use, short-lived, never stored in clear.</summary>
    public required string Token { get; init; }

    /// <summary>The contact's own account first, then each account trusting them.</summary>
    public required IReadOnlyList<AccountChoiceOption> Accounts { get; init; }
}

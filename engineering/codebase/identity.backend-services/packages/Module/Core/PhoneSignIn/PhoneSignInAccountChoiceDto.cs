namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;

/// <summary>Answers <c>phone-sign-in</c> with no session when the number's person is a trusted contact: the accounts to pick from.</summary>
public sealed record PhoneSignInAccountChoiceDto
{
    /// <summary>The choice; complete it with <c>phone-sign-in/choose</c>.</summary>
    public required AccountChoiceDto AccountChoice { get; init; }
}

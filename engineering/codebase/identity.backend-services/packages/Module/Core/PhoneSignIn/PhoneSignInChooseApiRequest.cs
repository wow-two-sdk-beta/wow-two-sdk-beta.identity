namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;

/// <summary>Picks the account a trusted contact's verified number signs in as.</summary>
public sealed record PhoneSignInChooseApiRequest
{
    /// <summary>The token of the <c>accountChoice</c> <c>phone-sign-in</c> answered with.</summary>
    public required string Token { get; init; }

    /// <summary>The account picked: the contact's own, or one trusting them.</summary>
    public required Guid UserId { get; init; }

    /// <summary>Whether the person asked to stay signed in, as <c>phone-sign-in</c> takes it.</summary>
    public bool IsPersistent { get; init; }
}

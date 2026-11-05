namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;

/// <summary>Polls a sign-in waiting for a trusted contact's press, and completes it once approved.</summary>
public sealed record PhoneSignInConfirmationApiRequest
{
    /// <summary>The token <c>deviceConfirmation</c> gave.</summary>
    public required string Token { get; init; }

    /// <summary>Whether the person asked to stay signed in, as <c>phone-sign-in</c> takes it.</summary>
    public bool IsPersistent { get; init; }
}

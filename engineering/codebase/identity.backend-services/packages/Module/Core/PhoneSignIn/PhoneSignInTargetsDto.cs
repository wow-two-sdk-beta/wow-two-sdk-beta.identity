namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;

/// <summary>Answers <c>phone-sign-in/targets</c>: the trusted contacts the number's code may go to; empty when none.</summary>
public sealed record PhoneSignInTargetsDto
{
    /// <summary>The contacts, masked, with opaque ids.</summary>
    public required IReadOnlyList<PhoneSignInTargetDto> TrustedContacts { get; init; }
}

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;

/// <summary>Asks which trusted contacts a sign-in by a number may send its code to.</summary>
public sealed record PhoneSignInTargetsApiRequest
{
    /// <summary>The number in E.164, such as <c>+998901234567</c>.</summary>
    public required string Phone { get; init; }
}

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.InviteSignUp;

/// <summary>Finishes an invitation sign-up with the code its trusted contact received.</summary>
public sealed record InviteSignUpVerifyApiRequest
{
    /// <summary>The code the contact passed on.</summary>
    public required string Code { get; init; }

    /// <summary>Whether the person asked to stay signed in, as <c>phone-sign-in</c> takes it.</summary>
    public bool IsPersistent { get; init; }
}

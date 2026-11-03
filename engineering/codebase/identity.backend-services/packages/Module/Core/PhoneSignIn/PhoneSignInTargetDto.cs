namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;

/// <summary>One trusted contact a sign-in's code may go to instead of the number's own chat.</summary>
public sealed record PhoneSignInTargetDto
{
    /// <summary>The opaque id <c>phone-sign-in/send</c> takes as <c>targetId</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The contact's masked name and phone.</summary>
    public required string Label { get; init; }
}

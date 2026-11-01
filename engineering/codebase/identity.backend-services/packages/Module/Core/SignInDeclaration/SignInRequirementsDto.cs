namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;

/// <summary>The verifications a sign-in needs before any token, from <c>Identity:SignIn</c>.</summary>
public sealed record SignInRequirementsDto
{
    /// <summary>Whether the email must be confirmed (<c>Identity:SignIn:IsConfirmedEmailRequired</c>).</summary>
    public bool IsConfirmedEmailRequired { get; init; }

    /// <summary>Whether the phone number must be confirmed (<c>Identity:SignIn:IsConfirmedPhoneNumberRequired</c>).</summary>
    public bool IsConfirmedPhoneNumberRequired { get; init; }

    /// <summary>Whether everyone needs a second factor; no setting requires one yet, so always false.</summary>
    public bool IsSecondFactorRequired { get; init; }
}

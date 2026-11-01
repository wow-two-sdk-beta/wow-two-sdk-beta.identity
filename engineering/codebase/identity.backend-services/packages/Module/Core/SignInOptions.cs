namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>Holds the preconditions the sign-in slice enforces.</summary>
public sealed record SignInOptions
{
    /// <summary>Refuse sign-in until the email is confirmed. Default false.</summary>
    public bool IsConfirmedEmailRequired { get; set; }

    /// <summary>Refuse sign-in until the phone number is confirmed. Default false.</summary>
    public bool IsConfirmedPhoneNumberRequired { get; set; }

    /// <summary>Accept an email address where a user name is expected. Default true.</summary>
    public bool IsEmailLoginAllowed { get; set; } = true;
}

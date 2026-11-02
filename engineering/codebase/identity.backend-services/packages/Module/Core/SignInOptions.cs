namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>Holds the preconditions the sign-in slice enforces.</summary>
/// <remarks>Set in code with <c>AddUserAccounts(o =&gt; o.SignIn…)</c> or in the host section <c>Identity:SignIn</c>, which is applied last.</remarks>
public sealed record SignInOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Identity:SignIn";

    /// <summary>
    /// Refuse sign-in until the email is confirmed; an account with no email but a confirmed phone (a phone sign-in's
    /// person) meets it through its phone. Default false.
    /// </summary>
    public bool IsConfirmedEmailRequired { get; set; }

    /// <summary>Refuse sign-in until the phone number is confirmed. Default false.</summary>
    public bool IsConfirmedPhoneNumberRequired { get; set; }

    /// <summary>Accept an email address where a user name is expected. Default true.</summary>
    public bool IsEmailIdentifierAllowed { get; set; } = true;
}

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;

/// <summary>
/// The sign-in declaration <c>GET sign-in-options</c> answers: how the hosted pages look, whether anyone may sign up, and
/// each enabled method in the order the pages lay them out, with its fields and their rules in the request's locale.
/// </summary>
public sealed record SignInOptionsDto
{
    /// <summary>The name the pages show, the instance owner's brand.</summary>
    public required string BrandName { get; init; }

    /// <summary>The fe sdk theme by its registry id, the instance's one look.</summary>
    public required string Theme { get; init; }

    /// <summary>The image behind the card, a same-origin path or an HTTPS URL; null for the theme's own backdrop.</summary>
    public string? BackgroundUrl { get; init; }

    /// <summary>
    /// Who may create an account (<c>Identity:SignUp:Mode</c>): <c>invite</c>, only through an invitation link, so the pages
    /// offer no sign-up without one; <c>open</c>, anyone.
    /// </summary>
    public required string SignUpMode { get; init; }

    /// <summary>
    /// Whether the sign-in forms offer "Keep me signed in": the sign-in request's <c>isPersistent</c> then takes the extended
    /// session lifetime (<c>Identity:Sessions</c>: fixed, with an extended lifetime). False while sessions slide, as a used
    /// session then stays signed in anyway.
    /// </summary>
    public bool IsPersistenceOffered { get; init; }

    /// <summary>The locale the rule messages are in: <c>uz</c>, <c>ru</c> or <c>en</c>.</summary>
    public required string Locale { get; init; }

    /// <summary>The locales the pages offer, in display order.</summary>
    public required IReadOnlyList<string> Locales { get; init; }

    /// <summary>
    /// Whether the instance takes emails (<c>Identity:Methods:Email</c>): sign-up by email and password, email confirmation
    /// and the forgot-password link. False on a phone-only instance, whose pages then ask for no email.
    /// </summary>
    public bool IsEmailEnabled { get; init; } = true;

    /// <summary>
    /// Whether accounts sign in through trusted contacts here: a code may go to an account's contact
    /// (<c>phone-sign-in/targets</c>), a contact's number may pick the account to sign in as, and an invitation's sign-up
    /// may verify through its contact. Needs phone sign-in and <c>Identity:TrustedContacts:IsEnabled</c>.
    /// </summary>
    public bool IsTrustedContactSignInEnabled { get; init; }

    /// <summary>The profile fields an invitation sign-up asks for beside the phone, with the picture's rules.</summary>
    public required SignUpProfileDto SignUpProfile { get; init; }

    /// <summary>The enabled sign-in methods, in the order the pages lay them out (<see cref="SignInMethodIdConstants"/>).</summary>
    public required IReadOnlyList<SignInMethodDto> Methods { get; init; }

    /// <summary>The verifications a sign-in needs before any token.</summary>
    public required SignInRequirementsDto Requirements { get; init; }
}

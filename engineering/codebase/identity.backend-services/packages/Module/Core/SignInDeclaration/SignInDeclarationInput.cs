using WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;
using WoW.Two.Sdk.Beta.Be.Identity.Core.ProfilePictures;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;

/// <summary>Holds the settings a declaration is built from, read once per request by <see cref="SignInDeclarationService"/>.</summary>
internal sealed record SignInDeclarationInput
{
    /// <summary>The instance owner's brand name.</summary>
    public required string BrandName { get; init; }

    /// <summary>The instance's theme.</summary>
    public required string Theme { get; init; }

    /// <summary>The instance's background; null for none.</summary>
    public string? BackgroundUrl { get; init; }

    /// <summary>Who may create an account (<c>Identity:SignUp:Mode</c>).</summary>
    public SignUpMode SignUpMode { get; init; } = SignUpMode.Invite;

    /// <summary>The password rules while password sign-in is on; null while it is off.</summary>
    public PasswordOptions? Password { get; init; }

    /// <summary>Whether the instance takes emails: sign-up by email, confirmation and reset links (<c>Identity:Methods:Email</c>).</summary>
    public bool IsEmailEnabled { get; init; } = true;

    /// <summary>The profile fields an invitation sign-up asks for (<c>Identity:SignUp:Profile</c>).</summary>
    public SignUpProfileOptions SignUpProfile { get; init; } = new();

    /// <summary>The picture rules while profile pictures are registered; null asks for no picture.</summary>
    public ProfilePictureOptions? PictureRules { get; init; }

    /// <summary>Whether an email signs in where a user name is expected.</summary>
    public bool IsEmailIdentifierAllowed { get; init; } = true;

    /// <summary>The allowed countries while phone sign-in is on, empty for every country; null while it is off.</summary>
    public IReadOnlyList<string>? PhoneCountries { get; init; }

    /// <summary>The length of a phone sign-in code (<c>Identity:Otp:CodeLength</c>).</summary>
    public int PhoneCodeLength { get; init; } = 6;

    /// <summary>Whether a sign-in may ask to stay signed in (<c>Identity:Sessions</c>: fixed, with an extended lifetime).</summary>
    public bool IsPersistenceOffered { get; init; }

    /// <summary>Whether a sign-in needs a confirmed email.</summary>
    public bool IsConfirmedEmailRequired { get; init; }

    /// <summary>Whether a sign-in needs a confirmed phone number.</summary>
    public bool IsConfirmedPhoneNumberRequired { get; init; }
}

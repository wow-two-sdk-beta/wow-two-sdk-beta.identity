namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;

/// <summary>
/// Holds the declaration's rule messages by locale, then by key: message templates whose <c>{Name}</c> placeholders fill
/// from the rule's operands. A password rule is keyed by the error code the server answers for it
/// (<see cref="IdentityErrorCodeConstants"/>), and its <c>en</c> text is the server's own, so both say the same; its
/// <c>ru</c> and <c>uz</c> texts come from <see cref="IdentityErrorTextConstants"/>. A key missing in a locale falls back
/// to <c>en</c>.
/// </summary>
public static class SignInDeclarationTextConstants
{
    /// <summary>The identifier field (user name or email) is empty.</summary>
    public const string IdentifierRequired = "IdentifierRequired";

    /// <summary>The identifier field is empty where only a user name signs in (<c>Identity:SignIn:IsEmailIdentifierAllowed</c> off).</summary>
    public const string UserNameIdentifierRequired = "UserNameIdentifierRequired";

    /// <summary>The sign-in form's password field is empty.</summary>
    public const string PasswordRequired = "PasswordRequired";

    /// <summary>The phone field is empty.</summary>
    public const string PhoneRequired = "PhoneRequired";

    /// <summary>The code field is empty.</summary>
    public const string CodeRequired = "CodeRequired";

    /// <summary>The code has the wrong length; <c>{Length}</c> is the code's.</summary>
    public const string CodeLength = "CodeLength";

    /// <summary>Gets the message templates per locale (<c>en</c>, <c>ru</c>, <c>uz</c>), then per key.</summary>
    /// <remarks>The <c>ru</c> and <c>uz</c> texts of the keys above are not written yet and fall back to <c>en</c>.</remarks>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Messages { get; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [IdentifierRequired] = "Enter your user name or email.",
                [UserNameIdentifierRequired] = "Enter your user name.",
                [PasswordRequired] = "Enter your password.",
                [PhoneRequired] = "Enter your phone number.",
                [CodeRequired] = "Enter the code.",
                [CodeLength] = "The code has {Length} characters.",
                [IdentityErrorCodeConstants.PasswordTooShort] = "Passwords must be at least {MinLength} characters.",
                [IdentityErrorCodeConstants.PasswordTooLong] = "Passwords must be at most {MaxLength} characters.",
                [IdentityErrorCodeConstants.PasswordRequiresDigit] = "Passwords must contain a digit.",
                [IdentityErrorCodeConstants.PasswordRequiresLower] = "Passwords must contain a lowercase letter.",
                [IdentityErrorCodeConstants.PasswordRequiresUpper] = "Passwords must contain an uppercase letter.",
                [IdentityErrorCodeConstants.PasswordRequiresNonAlphanumeric] = "Passwords must contain a symbol.",
                [IdentityErrorCodeConstants.PasswordRequiresUniqueChars] = "Passwords must use at least {RequiredUniqueChars} different characters.",
                [IdentityErrorCodeConstants.PasswordMatchesAccount] = "Passwords must differ from the user name and email.",
            },
            ["ru"] = new Dictionary<string, string>(StringComparer.Ordinal),
            ["uz"] = new Dictionary<string, string>(StringComparer.Ordinal),
        };
}

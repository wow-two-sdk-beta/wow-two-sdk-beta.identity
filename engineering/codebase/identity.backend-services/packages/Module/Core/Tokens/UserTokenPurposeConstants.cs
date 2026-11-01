namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Tokens;

/// <summary>Holds the purposes the identity slices bind their tokens to.</summary>
public static class UserTokenPurposeConstants
{
    /// <summary>Confirms the account's current email address.</summary>
    public const string EmailConfirmation = "EmailConfirmation";

    /// <summary>Authorizes one password reset.</summary>
    public const string PasswordReset = "PasswordReset";

    /// <summary>Carries a password-verified user into the two-factor step of a sign-in.</summary>
    public const string TwoFactorSignIn = "TwoFactorSignIn";

    /// <summary>Carries a person whose password matched but breaks the current rules to the change form; no other route accepts it.</summary>
    public const string PasswordChangeRequired = "PasswordChangeRequired";

    /// <summary>Authorizes a move to one new email address.</summary>
    public const string ChangeEmail = "ChangeEmail";

    /// <summary>Separates a purpose from the value it is scoped to, as in <c>ChangeEmail:{normalized address}</c>.</summary>
    public const char ScopeSeparator = ':';
}

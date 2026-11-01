namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;

/// <summary>Holds the error codes of a password change, beside <c>IdentityErrorCodeConstants</c>.</summary>
public static class PasswordChangeErrorCodeConstants
{
    /// <summary>The code the person entered is wrong, spent or expired, or none was entered while confirmation is a code.</summary>
    public const string InvalidCode = "InvalidPasswordChangeCode";

    /// <summary>The link's token is wrong, spent or expired, or links are not how this instance confirms a change.</summary>
    public const string InvalidLink = "InvalidPasswordChangeLink";

    /// <summary>The change must go through the link sent to the person, not the signed-in form.</summary>
    public const string LinkRequired = "PasswordChangeLinkRequired";

    /// <summary>A code or link was sent moments ago; the person waits before asking again.</summary>
    public const string RateLimited = "PasswordChangeRateLimited";

    /// <summary>The person has no verified channel to receive the code or link.</summary>
    public const string NoVerifiedChannel = "NoVerifiedChannel";

    /// <summary>The channel refused the code or link.</summary>
    public const string DeliveryFailed = "PasswordChangeDeliveryFailed";

    /// <summary>The password broke the current rules, so the person must change it before signing in.</summary>
    public const string ChangeRequired = "PasswordChangeRequired";
}

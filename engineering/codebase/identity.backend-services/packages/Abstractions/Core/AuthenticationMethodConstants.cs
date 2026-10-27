namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>
/// Holds the authentication-method claim (<c>amr</c>, RFC 8176) and the values a sign-in records in it, so an app can
/// ask for step-up, such as a fresh passkey (<c>hwk</c>) before a destructive action.
/// </summary>
public static class AuthenticationMethodConstants
{
    /// <summary>The claim type, one claim per method used (<c>amr</c>).</summary>
    public const string ClaimType = "amr";

    /// <summary>A password (<c>pwd</c>).</summary>
    public const string Password = "pwd";

    /// <summary>A one-time code: authenticator, delivered code, recovery code or email link (<c>otp</c>).</summary>
    public const string OneTimePassword = "otp";

    /// <summary>More than one factor (<c>mfa</c>).</summary>
    public const string MultipleFactors = "mfa";

    /// <summary>A hardware-protected key: a passkey (<c>hwk</c>).</summary>
    public const string HardwareKey = "hwk";
}

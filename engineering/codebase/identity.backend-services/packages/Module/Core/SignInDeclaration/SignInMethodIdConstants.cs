namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;

/// <summary>Names the sign-in methods the declaration lists; <c>fe-identity</c> has one component per id.</summary>
public static class SignInMethodIdConstants
{
    /// <summary>An identifier (user name or email) and password (<c>PasswordSignIn</c>).</summary>
    public const string Password = "password";

    /// <summary>A code sent to a phone's linked channel, the instance's Telegram bot (<c>PhoneCodeSignIn</c>).</summary>
    public const string PhoneCode = "phone-code";
}

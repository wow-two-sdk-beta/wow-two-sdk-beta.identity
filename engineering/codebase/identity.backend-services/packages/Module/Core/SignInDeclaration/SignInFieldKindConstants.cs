namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;

/// <summary>Names the inputs a declared field takes.</summary>
public static class SignInFieldKindConstants
{
    /// <summary>A line of text, such as a user name or email.</summary>
    public const string Text = "text";

    /// <summary>A masked password.</summary>
    public const string Password = "password";

    /// <summary>A phone number in E.164, typed through the fe sdk's <c>PhoneInput</c>.</summary>
    public const string Phone = "phone";

    /// <summary>A short one-time code.</summary>
    public const string Pin = "pin";
}

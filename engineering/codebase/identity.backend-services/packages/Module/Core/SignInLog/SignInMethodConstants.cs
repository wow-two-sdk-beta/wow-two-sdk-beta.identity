namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;

/// <summary>Names the sign-in steps the log records in <c>method</c>.</summary>
public static class SignInMethodConstants
{
    /// <summary>A login and password.</summary>
    public const string Password = "password";

    /// <summary>A passkey assertion.</summary>
    public const string Passkey = "passkey";

    /// <summary>A single-use link sent by email.</summary>
    public const string EmailLink = "email_link";

    /// <summary>A single-use code sent by email.</summary>
    public const string EmailCode = "email_code";

    /// <summary>A sign-in completed elsewhere, such as an external provider.</summary>
    public const string External = "external";

    /// <summary>The prefix of a second-factor step, followed by its method: <c>2fa:authenticator</c>, <c>2fa:sms</c>, <c>2fa:recovery_code</c>.</summary>
    public const string SecondFactorPrefix = "2fa:";

    /// <summary>A recovery code as the second factor.</summary>
    public const string RecoveryCode = SecondFactorPrefix + "recovery_code";

    /// <summary>The second-factor method name for <paramref name="method"/>, such as <c>2fa:sms</c>.</summary>
    /// <param name="method">The two-factor method, such as <c>authenticator</c> or <c>sms</c>.</param>
    public static string SecondFactor(string method) => SecondFactorPrefix + method;
}

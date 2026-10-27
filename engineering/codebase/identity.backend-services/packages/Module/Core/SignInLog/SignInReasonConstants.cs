namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;

/// <summary>Names why a logged sign-in attempt did not succeed, in <c>reason</c>.</summary>
public static class SignInReasonConstants
{
    /// <summary>No account answers to the login.</summary>
    public const string UnknownAccount = "unknown_account";

    /// <summary>The password did not match.</summary>
    public const string WrongPassword = "wrong_password";

    /// <summary>The second-factor code did not match.</summary>
    public const string WrongCode = "wrong_code";

    /// <summary>The passkey assertion did not verify.</summary>
    public const string InvalidPasskey = "invalid_passkey";

    /// <summary>The two-factor ticket was missing, expired or forged.</summary>
    public const string InvalidTicket = "invalid_ticket";

    /// <summary>The email link or code was invalid, spent or expired.</summary>
    public const string InvalidLinkOrCode = "invalid_link_or_code";

    /// <summary>The account was already inside a lockout window.</summary>
    public const string LockedOut = "locked_out";

    /// <summary>This failure reached the limit and started a lockout.</summary>
    public const string LockoutStarted = "lockout_started";

    /// <summary>The account's email is unconfirmed while sign-in requires it.</summary>
    public const string UnconfirmedEmail = "unconfirmed_email";

    /// <summary>The account's phone number is unconfirmed while sign-in requires it.</summary>
    public const string UnconfirmedPhone = "unconfirmed_phone";
}

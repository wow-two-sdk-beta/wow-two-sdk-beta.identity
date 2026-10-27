namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;

/// <summary>Names how a logged sign-in attempt ended, in <c>result</c>.</summary>
public static class SignInResultConstants
{
    /// <summary>The person is signed in.</summary>
    public const string Succeeded = "succeeded";

    /// <summary>The step was refused; <c>reason</c> says why.</summary>
    public const string Failed = "failed";

    /// <summary>The account is locked out, or this attempt locked it.</summary>
    public const string LockedOut = "locked_out";

    /// <summary>The credentials were right, but a precondition is unmet.</summary>
    public const string NotAllowed = "not_allowed";

    /// <summary>The first factor was right; a second factor must follow.</summary>
    public const string TwoFactorRequired = "two_factor_required";
}

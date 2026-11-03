namespace WoW.Two.Sdk.Beta.Be.Identity.Core.InviteSignUp;

/// <summary>Refers to how verifying an invitation sign-up with its trusted contact's code ended.</summary>
public enum InviteSignUpVerifyStatus
{
    /// <summary>The person was created with no phone, joined, and has the contact recorded; sign the browser in.</summary>
    Completed,

    /// <summary>The code is wrong, spent, expired or out of tries.</summary>
    InvalidCode,

    /// <summary>No sign-up through a trusted contact waits in this browser.</summary>
    NotFound,

    /// <summary>The sign-up stopped waiting.</summary>
    Expired,

    /// <summary>The invitation refused the join; <see cref="InviteSignUpVerifyResult.FailureCode"/> says why.</summary>
    Failed,
}

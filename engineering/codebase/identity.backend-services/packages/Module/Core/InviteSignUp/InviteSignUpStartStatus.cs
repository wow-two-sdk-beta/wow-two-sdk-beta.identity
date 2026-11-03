namespace WoW.Two.Sdk.Beta.Be.Identity.Core.InviteSignUp;

/// <summary>Refers to how starting an invitation sign-up ended.</summary>
public enum InviteSignUpStartStatus
{
    /// <summary>The sign-up waits for the bot; the browser opens the start link.</summary>
    Started,

    /// <summary>No invitation matches the token, or it no longer redeems; <see cref="InviteSignUpStartResult.Refusal"/> says which.</summary>
    InvitationRefused,

    /// <summary>The name or picture breaks the sign-up's profile rules; <see cref="InviteSignUpStartResult.Refusal"/> carries the code.</summary>
    Invalid,

    /// <summary>A start link went out moments ago from the same origin, or for the invitation's number.</summary>
    RateLimited,

    /// <summary>The instance's bot is off, so no phone can be verified.</summary>
    Unavailable,

    /// <summary>
    /// A sign-up through the trusted contact cannot go ahead: trusted contacts are off, the invitation names none, or the
    /// contact cannot receive a code now; <see cref="InviteSignUpStartResult.Refusal"/> carries the code.
    /// </summary>
    TrustedContactRefused,
}

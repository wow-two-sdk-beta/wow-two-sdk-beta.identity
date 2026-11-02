namespace WoW.Two.Sdk.Beta.Be.Identity.Core.InviteSignUp;

/// <summary>Refers to where an invitation sign-up stands for the browser that started it.</summary>
public enum InviteSignUpState
{
    /// <summary>It waits for the shared contact in the bot.</summary>
    Pending,

    /// <summary>The contact proved the phone and the person joined.</summary>
    Completed,

    /// <summary>The bot refused the join; <see cref="InviteSignUpPoll.FailureCode"/> says why.</summary>
    Failed,

    /// <summary>It stopped waiting before a contact arrived.</summary>
    Expired,

    /// <summary>No sign-up matches the browser's binding.</summary>
    NotFound,
}

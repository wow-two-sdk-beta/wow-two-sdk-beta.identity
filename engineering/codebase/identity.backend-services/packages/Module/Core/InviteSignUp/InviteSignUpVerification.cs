namespace WoW.Two.Sdk.Beta.Be.Identity.Core.InviteSignUp;

/// <summary>Refers to how an invitation sign-up proves the person: the form's <c>verification</c> field.</summary>
public enum InviteSignUpVerification
{
    /// <summary>The person shares their own contact in the bot (<c>telegram</c>, the default); the account holds that number.</summary>
    Telegram,

    /// <summary>A code goes to the invitation's trusted contact (<c>trusted-contact</c>); the account holds no phone.</summary>
    TrustedContact,
}

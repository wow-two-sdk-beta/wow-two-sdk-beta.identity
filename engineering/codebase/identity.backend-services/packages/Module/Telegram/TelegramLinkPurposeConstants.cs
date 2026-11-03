namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>Names what a completed start link leads to; each purpose has one <see cref="ITelegramLinkCompletionHandler"/>.</summary>
public static class TelegramLinkPurposeConstants
{
    /// <summary>Sign in by phone: the bot sends the pending sign-in code once the number is linked.</summary>
    public const string PhoneSignIn = "phone-sign-in";

    /// <summary>Sign up from an invitation: the shared contact names the number, which proves it, and the person joins.</summary>
    public const string InviteSignUp = "invite-sign-up";

    /// <summary>Add or change one's own phone: the shared contact names the number, which becomes the account's confirmed phone.</summary>
    public const string AddPhone = "add-phone";
}

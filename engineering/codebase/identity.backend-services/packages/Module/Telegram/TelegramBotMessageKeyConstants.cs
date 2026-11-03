namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>Names the texts the bot sends besides codes and notices; <see cref="TelegramBotOptions.Messages"/> overrides them per culture.</summary>
public static class TelegramBotMessageKeyConstants
{
    /// <summary>Asks the person to share their number with the button, after a valid start link.</summary>
    public const string SharePrompt = "share-prompt";

    /// <summary>The share-contact button's label.</summary>
    public const string ShareButton = "share-button";

    /// <summary>A shared contact belongs to someone else.</summary>
    public const string NotOwnContact = "not-own-contact";

    /// <summary>The shared number differs from the one the link was made for.</summary>
    public const string NumberMismatch = "number-mismatch";

    /// <summary>The link is unknown, spent or expired, or no link is pending.</summary>
    public const string LinkExpired = "link-expired";

    /// <summary>A plain <c>/start</c> without a link token.</summary>
    public const string Welcome = "welcome";

    /// <summary>A code went out moments ago, so none followed the shared contact.</summary>
    public const string CodeRecentlySent = "code-recently-sent";

    /// <summary>Asks for the contact that finishes an invitation sign-up.</summary>
    public const string SignUpSharePrompt = "sign-up-share-prompt";

    /// <summary>The contact proved the number and the person joined; the sign-up page continues by itself.</summary>
    public const string SignUpDone = "sign-up-done";

    /// <summary>The invitation was spent, withdrawn or expired before the contact arrived.</summary>
    public const string SignUpFailed = "sign-up-failed";

    /// <summary>The invitation names another number than the shared contact's.</summary>
    public const string InvitationMismatch = "invitation-mismatch";

    /// <summary>The shared contact's number is outside the instance's allowed countries.</summary>
    public const string CountryNotAllowed = "country-not-allowed";

    /// <summary>
    /// The number is linked to another Telegram account and a person holds it, so the link stays until an owner or admin of
    /// the person's organization approves the change.
    /// </summary>
    public const string LinkChangePending = "link-change-pending";

    /// <summary>The one-line stub a code or notice becomes when the sweep can no longer delete it (48 hours or older).</summary>
    public const string MessageExpired = "message-expired";

    /// <summary>A pressed button is unknown, spent or gone with its message.</summary>
    public const string ButtonExpired = "button-expired";

    /// <summary>A button was pressed by another Telegram user than the one allowed to.</summary>
    public const string ButtonNotYours = "button-not-yours";

    /// <summary>A pressed button's action failed.</summary>
    public const string ButtonFailed = "button-failed";

    /// <summary>A sign-in code sent to a trusted contact for an account: <c>{code}</c>, <c>{account}</c>, <c>{app}</c>, <c>{minutes}</c>.</summary>
    public const string ContactSignInCode = "contact-code-sign-in";

    /// <summary>A sign-up code sent to the trusted contact an invitation names: <c>{code}</c>, <c>{account}</c>, <c>{app}</c>, <c>{minutes}</c>.</summary>
    public const string ContactSignUpCode = "contact-code-sign-up";

    /// <summary>The notice to an account's own chat after a sign-in through one of its contacts: <c>{contact}</c>, <c>{app}</c>, <c>{time}</c>.</summary>
    public const string ContactSignInNotice = "contact-sign-in-notice";

    /// <summary>The label of the notice's button that revokes the contact.</summary>
    public const string RevokeTrustButton = "revoke-trust-button";

    /// <summary>What the notice reads, and the presser's toast, once its button revoked the contact.</summary>
    public const string TrustRevoked = "trust-revoked";

    /// <summary>Asks for the contact that adds or changes one's own phone.</summary>
    public const string AddPhoneSharePrompt = "add-phone-share-prompt";

    /// <summary>The shared number is now the account's own phone.</summary>
    public const string AddPhoneDone = "add-phone-done";

    /// <summary>Another account holds the shared number confirmed.</summary>
    public const string AddPhoneTaken = "add-phone-taken";

    /// <summary>The add-phone link no longer adds a phone: the account or its session is gone, or it gained an own phone meanwhile.</summary>
    public const string AddPhoneFailed = "add-phone-failed";
}

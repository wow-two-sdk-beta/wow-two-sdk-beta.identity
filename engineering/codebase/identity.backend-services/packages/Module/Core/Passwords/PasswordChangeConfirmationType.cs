namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;

/// <summary>How a password change is confirmed before it takes effect (<c>Identity:Password:Change:Confirmation</c>).</summary>
public enum PasswordChangeConfirmationType
{
    /// <summary>The current password is the only proof; "Change password" opens the form at once.</summary>
    None,

    /// <summary>A one-time code, sent to the person's verified channel, is entered beside the new password.</summary>
    Code,

    /// <summary>A one-time link, sent to the person's verified channel, is the only way to the change form.</summary>
    Link,
}

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>Holds whether accounts sign in through trusted contacts, and how many one account may have.</summary>
/// <remarks>
/// Set in code with <c>.AddTrustedContacts(o => …)</c> or in the host section <c>Identity:TrustedContacts</c>, which is applied
/// last and read live. On, it needs the instance's Telegram bot (<c>Identity:Telegram:IsEnabled</c>); a boot refuses it
/// otherwise, and <c>config check</c> reports it.
/// </remarks>
public sealed record TrustedContactOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Identity:TrustedContacts";

    /// <summary>The highest <see cref="MaxPerPerson"/> a setting may ask for.</summary>
    public const int MaxPerPersonLimit = 10;

    /// <summary>
    /// Gets or sets whether trusted contacts run: an invitation names one, a sign-up and a sign-in go through one, and a
    /// contact's number offers the accounts trusting it. Off, recorded contacts stay but no code reaches them. Default <c>false</c>.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>Gets or sets how many trusted contacts one account may have, 1–<see cref="MaxPerPersonLimit"/>. Default 3.</summary>
    public int MaxPerPerson { get; set; } = 3;

    /// <summary>
    /// Gets or sets whether a browser an account hasn't used waits for a contact's press before a sign-in by the account's
    /// own code. Read only while trusted contacts are on; an account with no deliverable contact is never asked. Default <c>true</c>.
    /// </summary>
    public bool IsNewDeviceConfirmationRequired { get; set; } = true;
}

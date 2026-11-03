namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>Names the trusted contact failures the account and organization APIs report in <c>identityCode</c> and <c>messageKey</c>.</summary>
public static class TrustedContactErrorCodeConstants
{
    /// <summary>The change needs a session signed in with the account's own phone (<c>403</c>).</summary>
    public const string MainPhoneRequired = "MainPhoneRequired";

    /// <summary>The contact is the last one of an account with no own phone, so they cannot leave (<c>409</c>).</summary>
    public const string LastTrustedContact = "LastTrustedContact";

    /// <summary>No such trusted contact, target or trusting account (<c>404</c>).</summary>
    public const string TrustedContactNotFound = "TrustedContactNotFound";

    /// <summary>The contact cannot receive codes now: no confirmed phone linked to the bot, or the chat refused the bot (<c>409</c>).</summary>
    public const string TrustedContactUnavailable = "TrustedContactUnavailable";

    /// <summary>An invitation's contact is neither the inviter nor a member of the organization (<c>400</c>).</summary>
    public const string InvalidTrustedContact = "InvalidTrustedContact";

    /// <summary>Trusted contacts are off on this instance (<c>Identity:TrustedContacts:IsEnabled</c>) (<c>404</c>).</summary>
    public const string TrustedContactsOff = "TrustedContactsOff";

    /// <summary>A sign-up asked to verify through a trusted contact, but its invitation names none (<c>400</c>).</summary>
    public const string NoTrustedContact = "NoTrustedContact";

    /// <summary>An account choice token is unknown, spent or expired, or does not offer the account picked (<c>401</c>).</summary>
    public const string InvalidAccountChoice = "InvalidAccountChoice";

    /// <summary>The account already has <c>Identity:TrustedContacts:MaxPerPerson</c> contacts.</summary>
    public const string TooManyTrustedContacts = "TooManyTrustedContacts";

    /// <summary>The account holds an instance role, which keeps it from having a trusted contact in slice 1.</summary>
    public const string InstanceRoleHolder = "InstanceRoleHolder";
}

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>Refers to how a contact's leaving an account ended.</summary>
public enum TrustedContactLeaveStatus
{
    /// <summary>The contact left; the sessions started through them ended.</summary>
    Left,

    /// <summary>The account does not trust the person.</summary>
    NotFound,

    /// <summary>The person is the last contact of an account with no own phone, so they stay.</summary>
    LastContact,
}

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>Refers to how sending a code to a trusted contact ended.</summary>
public enum TrustedContactSendStatus
{
    /// <summary>The code went to the contact's Telegram.</summary>
    CodeSent,

    /// <summary>The number is no E.164 number.</summary>
    InvalidNumber,

    /// <summary>The number belongs to no allowed country.</summary>
    CountryNotAllowed,

    /// <summary>A code went out moments ago for the number or to the contact, or one of them spent its failure budget.</summary>
    RateLimited,

    /// <summary>No account holding the number has the contact the target names, or trusted contacts are off.</summary>
    NotFound,

    /// <summary>The contact cannot receive the code: no confirmed phone linked to the bot, or the chat refused it.</summary>
    Unavailable,
}

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>Refers to how asking for an <c>add-phone</c> start link ended.</summary>
public enum OwnPhoneLinkStatus
{
    /// <summary>The link was made; the person opens it and shares their own contact in the bot.</summary>
    Created,

    /// <summary>The account has an own phone, and changing it needs a session signed in with that phone.</summary>
    MainPhoneRequired,

    /// <summary>A link went out moments ago from the same address.</summary>
    RateLimited,

    /// <summary>The instance's bot is off.</summary>
    Unavailable,
}

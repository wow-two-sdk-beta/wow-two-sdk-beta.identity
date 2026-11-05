namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>What a contact's press on a new device notice led to.</summary>
public enum DeviceDecision
{
    /// <summary>The browser is approved.</summary>
    Approved,

    /// <summary>The sign-in is stopped.</summary>
    Denied,

    /// <summary>Nothing changed: the request ended, was answered, or the presser is no longer a contact.</summary>
    NoLongerOpen,
}

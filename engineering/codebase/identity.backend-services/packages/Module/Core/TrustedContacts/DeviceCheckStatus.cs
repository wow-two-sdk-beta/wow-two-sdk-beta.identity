namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>What a code that checked out may do on this device.</summary>
public enum DeviceCheckStatus
{
    /// <summary>Sign in: the device is known, or nobody can answer for the account.</summary>
    Passed,

    /// <summary>Wait: the account's contacts were asked to press.</summary>
    Waiting,

    /// <summary>Refuse for now: every contact's chat refused the notice.</summary>
    Unavailable,
}

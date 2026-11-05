namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>Where a sign-in waiting for a contact's press stands.</summary>
public enum DeviceConfirmationState
{
    /// <summary>Nobody answered yet.</summary>
    Pending,

    /// <summary>A contact approved; this answer spent the token, so sign in now.</summary>
    Approved,

    /// <summary>A contact denied.</summary>
    Denied,

    /// <summary>The token is unknown, spent, replaced or expired.</summary>
    Gone,
}

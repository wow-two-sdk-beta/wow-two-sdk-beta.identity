using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>
/// One browser an account may sign in from with no trusted contact's press (<c>identity_known_devices</c>,
/// <c>011-device-confirmations</c>): a contact approved it, or a sign-in went through a contact on it. One row per account
/// and browser (composite key user + device).
/// </summary>
public sealed class IdentityKnownDevice : ICompositeKeyEntity
{
    /// <summary>The account.</summary>
    public Guid UserId { get; set; }

    /// <summary>The hex SHA-256 of the browser's <c>__Host-identity-device</c> cookie.</summary>
    public string DeviceHash { get; set; } = string.Empty;

    /// <summary>The contact who answered for the browser: pressed Approve, or received the code it signed in with.</summary>
    public Guid ConfirmedByUserId { get; set; }

    /// <summary>When the browser became known (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

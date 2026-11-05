using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>
/// One sign-in waiting for a trusted contact's press (<c>identity_device_confirmations</c>,
/// <c>011-device-confirmations</c>): the account's own code checked out on a browser the account hasn't used. A new request
/// from the account replaces its open one; an approved row goes when the page signs in with it.
/// </summary>
public sealed class IdentityDeviceConfirmation : IKeyedEntity<Guid>
{
    /// <summary>Primary key, a Guid v7 made in code; the notice's buttons name it.</summary>
    public Guid Id { get; set; }

    /// <summary>The account signing in.</summary>
    public Guid UserId { get; set; }

    /// <summary>The hex SHA-256 of the waiting browser's device cookie, or of a one-off value for a bearer client.</summary>
    public string DeviceHash { get; set; } = string.Empty;

    /// <summary>The hex SHA-256 of the token the page polls with.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>The contact's answer; null while nobody answered.</summary>
    public bool? IsApproved { get; set; }

    /// <summary>The contact who answered; null while nobody answered.</summary>
    public Guid? DecidedByUserId { get; set; }

    /// <summary>When the contact answered (UTC); null while nobody answered.</summary>
    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>When the request was made (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the request stops counting (UTC), answered or not.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

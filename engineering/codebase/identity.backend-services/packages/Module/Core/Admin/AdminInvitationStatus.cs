namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;

/// <summary>Refers to how <see cref="AdminInvitationService.CreateAsync"/> ended.</summary>
public enum AdminInvitationStatus
{
    /// <summary>The invitation was made.</summary>
    Created,

    /// <summary>No organization has the handle or id; nothing changed.</summary>
    Refused,

    /// <summary>The request itself is invalid, such as a role outside the catalog or a malformed phone; nothing changed.</summary>
    Invalid,
}

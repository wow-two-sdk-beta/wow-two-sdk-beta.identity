namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;

/// <summary>Refers to how <see cref="FirstOwnerService.CreateAsync"/> ended.</summary>
public enum FirstOwnerStatus
{
    /// <summary>The organization, the owner or both were created; the owner holds the owner role.</summary>
    Created,

    /// <summary>Both existed already, the person as an owner of the organization; nothing changed.</summary>
    Present,

    /// <summary>A conflict: the handle, the id, the email or the phone belongs to someone else; nothing changed.</summary>
    Refused,

    /// <summary>The request itself is invalid, such as a missing name or a malformed phone; nothing changed.</summary>
    Invalid,
}

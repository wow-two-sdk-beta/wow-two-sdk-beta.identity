namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;

/// <summary>Refers to how <see cref="InstanceAdminService.CreateAsync"/> ended.</summary>
public enum InstanceAdminStatus
{
    /// <summary>The person was created, given the instance role <c>admin</c>, or both.</summary>
    Created,

    /// <summary>The named person holds <c>admin</c> already; nothing changed.</summary>
    Present,

    /// <summary>A conflict: another person holds <c>admin</c>, or the id, the email or the phone belongs to someone else; nothing changed.</summary>
    Refused,

    /// <summary>The request itself is invalid, such as a missing name or a malformed phone; nothing changed.</summary>
    Invalid,
}

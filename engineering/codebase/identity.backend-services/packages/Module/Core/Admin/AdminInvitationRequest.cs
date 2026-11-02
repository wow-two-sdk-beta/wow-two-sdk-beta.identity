namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;

/// <summary>Represents an invitation the host CLI makes, as <c>admin invite</c> names it.</summary>
public sealed record AdminInvitationRequest
{
    /// <summary>Gets the organization, by its handle (such as <c>ocharo</c>) or its uuid.</summary>
    public required string Organization { get; init; }

    /// <summary>Gets the organization role the person joins with, such as <c>owner</c>.</summary>
    public required string Role { get; init; }

    /// <summary>Gets the phone, as typed, that alone may redeem it; null for none.</summary>
    public string? PhoneNumber { get; init; }
}

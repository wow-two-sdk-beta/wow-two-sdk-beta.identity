namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

/// <summary>Makes an invitation link into the organization.</summary>
public sealed record InvitationApiRequest
{
    /// <summary>The role the person joins with, one of the organization roles, owner included.</summary>
    public required string Role { get; init; }

    /// <summary>The phone number that alone may redeem the link, international; null for anyone holding it.</summary>
    public string? PhoneNumber { get; init; }

    /// <summary>The email that alone may redeem the link; null for anyone holding it.</summary>
    public string? Email { get; init; }
}

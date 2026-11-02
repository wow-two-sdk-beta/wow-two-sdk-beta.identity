namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>
/// One invitation into an organization (<c>identity_invitations</c>): a link carrying a role the person cannot change, made
/// by an owner or admin. It signs a new person up even while sign-up is by invitation only, or adds a signed-in person, once.
/// An invitation naming a phone number or an email redeems only for that identifier. Only the token's SHA-256 is stored.
/// </summary>
public sealed class IdentityInvitation
{
    /// <summary>Primary key, a Guid v7 made in code.</summary>
    public Guid Id { get; set; }

    /// <summary>The organization the person joins.</summary>
    public Guid OrganizationId { get; set; }

    /// <summary>The role the person joins with, one of the organization roles.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>The hex SHA-256 of the link's token.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>The phone number, in E.164, that alone may redeem it; null when it does not name one.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>The email, lower case, that alone may redeem it; null when it does not name one.</summary>
    public string? Email { get; set; }

    /// <summary>The owner or admin who made it.</summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>When it was made (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When it stops working (UTC).</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>When a person redeemed it; null while unused.</summary>
    public DateTimeOffset? AcceptedAt { get; set; }

    /// <summary>The person who redeemed it; null while unused.</summary>
    public Guid? AcceptedByUserId { get; set; }

    /// <summary>When an owner or admin withdrew it; null while standing.</summary>
    public DateTimeOffset? RevokedAt { get; set; }
}

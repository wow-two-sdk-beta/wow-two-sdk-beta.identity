namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

/// <summary>One invitation into an organization, as its owners and admins see it; never its token.</summary>
public sealed record InvitationDto
{
    /// <summary>The invitation's id.</summary>
    public required Guid Id { get; init; }

    /// <summary>The role the person joins with.</summary>
    public required string Role { get; init; }

    /// <summary>Where it stands: <c>pending</c>, <c>expired</c>, <c>accepted</c> or <c>revoked</c>.</summary>
    public required string Status { get; init; }

    /// <summary>The phone number that alone may redeem it; null when it names none.</summary>
    public string? PhoneNumber { get; init; }

    /// <summary>The email that alone may redeem it; null when it names none.</summary>
    public string? Email { get; init; }

    /// <summary>When it was made (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When it stops working (UTC).</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>When a person redeemed it; null while unused.</summary>
    public DateTimeOffset? AcceptedAt { get; init; }

    /// <summary>The person who redeemed it; null while unused.</summary>
    public Guid? AcceptedByUserId { get; init; }

    /// <summary>The trusted contact a sign-up through it gets its code from; null when it names none.</summary>
    public Guid? TrustedContactUserId { get; init; }
}

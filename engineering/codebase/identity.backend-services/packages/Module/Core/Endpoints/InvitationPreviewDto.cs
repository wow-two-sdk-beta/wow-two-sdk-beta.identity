namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>What an invitation link offers, as <c>GET invitations/{token}</c> answers it before anyone signs up.</summary>
public sealed record InvitationPreviewDto
{
    /// <summary>The organization the person joins.</summary>
    public required string OrganizationName { get; init; }

    /// <summary>The role the person joins with; the link fixes it.</summary>
    public required string Role { get; init; }

    /// <summary>Where it stands: <c>pending</c>, <c>expired</c>, <c>accepted</c> or <c>revoked</c>; only a pending one redeems.</summary>
    public required string Status { get; init; }

    /// <summary>A hint of the phone number that alone may redeem it (<c>+998*******45</c>); null when it names none.</summary>
    public string? PhoneNumberHint { get; init; }

    /// <summary>A hint of the email that alone may redeem it (<c>a***@example.com</c>); null when it names none.</summary>
    public string? EmailHint { get; init; }

    /// <summary>When it stops working (UTC).</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// The masked name of the trusted contact a sign-up through it gets its code from (<c>S*** R***</c>), or their masked
    /// phone when they set no name; null when it names none or trusted contacts are off.
    /// </summary>
    public string? TrustedContactHint { get; init; }
}

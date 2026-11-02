namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

/// <summary>One member of an organization, as its owners and admins see them.</summary>
public sealed record OrganizationMemberDto
{
    /// <summary>The person's id, the <c>sub</c> of their tokens.</summary>
    public required Guid UserId { get; init; }

    /// <summary>The name the person goes by; null when they set none.</summary>
    public string? DisplayName { get; init; }

    /// <summary>The person's user name.</summary>
    public string? UserName { get; init; }

    /// <summary>The person's email; null without one.</summary>
    public string? Email { get; init; }

    /// <summary>The person's phone number in E.164; null without one.</summary>
    public string? PhoneNumber { get; init; }

    /// <summary>The person's role in the organization.</summary>
    public required string Role { get; init; }

    /// <summary>When the person joined (UTC).</summary>
    public required DateTimeOffset JoinedAt { get; init; }
}

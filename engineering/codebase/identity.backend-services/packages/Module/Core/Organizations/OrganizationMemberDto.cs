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

    /// <summary>
    /// Whether another Telegram account shared the person's confirmed phone number within the last day and waits for an
    /// owner or admin to approve or deny moving the number's link to it; false while the instance has no bot.
    /// </summary>
    public bool HasPendingTelegramChange { get; init; }

    /// <summary>Whether the person's phone number is confirmed, so they may be named an invitation's trusted contact once it is linked to the bot.</summary>
    public bool IsPhoneConfirmed { get; init; }

    /// <summary>The person's trusted contacts, oldest first; empty without any.</summary>
    public IReadOnlyList<MemberTrustedContactDto> TrustedContacts { get; init; } = [];
}

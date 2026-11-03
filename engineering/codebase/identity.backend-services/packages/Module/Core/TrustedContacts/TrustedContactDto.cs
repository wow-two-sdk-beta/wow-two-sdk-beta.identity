namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>One trusted contact of the signed-in account.</summary>
public sealed record TrustedContactDto
{
    /// <summary>The contact; <c>DELETE manage/trusted-contacts/{userId}</c> revokes them.</summary>
    public required Guid UserId { get; init; }

    /// <summary>The name the contact goes by, or their masked phone when they set none.</summary>
    public string? DisplayName { get; init; }

    /// <summary>The contact's masked phone; null without one.</summary>
    public string? PhoneHint { get; init; }

    /// <summary>Whether a code can reach the contact now: their phone is confirmed and linked to the bot.</summary>
    public required bool IsDeliverable { get; init; }

    /// <summary>When the contact was recorded (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}

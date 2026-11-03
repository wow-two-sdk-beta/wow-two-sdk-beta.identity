namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>One trusted contact of an account, as its owner sees it.</summary>
public sealed record TrustedContactModel
{
    /// <summary>The contact.</summary>
    public required Guid UserId { get; init; }

    /// <summary>The name the contact goes by, or their masked phone when they set none.</summary>
    public string? DisplayName { get; init; }

    /// <summary>The contact's masked phone (<c>+998*******45</c>); null without one.</summary>
    public string? PhoneHint { get; init; }

    /// <summary>Whether a code can reach the contact now: their phone is confirmed and linked to the current bot.</summary>
    public required bool IsDeliverable { get; init; }

    /// <summary>When the contact was recorded (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}

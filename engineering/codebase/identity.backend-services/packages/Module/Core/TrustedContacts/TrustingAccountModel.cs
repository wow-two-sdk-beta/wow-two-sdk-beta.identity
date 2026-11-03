namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>One account trusting a person as its contact, as the contact sees it.</summary>
public sealed record TrustingAccountModel
{
    /// <summary>The account.</summary>
    public required Guid UserId { get; init; }

    /// <summary>The name the account goes by, or its masked phone when it set none.</summary>
    public string? DisplayName { get; init; }

    /// <summary>When the contact was recorded (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Whether the contact may leave: false while they are the last contact of an account with no own phone.</summary>
    public required bool CanLeave { get; init; }
}

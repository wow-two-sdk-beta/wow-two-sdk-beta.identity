namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>One account trusting the signed-in person as its contact.</summary>
public sealed record TrustedByDto
{
    /// <summary>The account; <c>DELETE manage/trusted-by/{userId}</c> leaves it.</summary>
    public required Guid UserId { get; init; }

    /// <summary>The name the account goes by, or its masked phone when it set none.</summary>
    public string? DisplayName { get; init; }

    /// <summary>When the contact was recorded (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Whether the person may leave: false while they are the last contact of an account with no own phone.</summary>
    public required bool CanLeave { get; init; }
}

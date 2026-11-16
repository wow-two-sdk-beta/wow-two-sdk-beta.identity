namespace Identity.Api.Settings;

/// <summary>Names the one service client and its dedicated audience for remote account deletion.</summary>
public sealed record AccountDeletionSettings
{
    /// <summary>The host section for the product's deletion client.</summary>
    public const string SectionName = "Identity:AccountDeletion";

    /// <summary>Gets the client identifier of the product service.</summary>
    public string? ClientId { get; init; }

    /// <summary>Gets the absolute HTTPS resource audience dedicated to account deletion.</summary>
    public Uri? Audience { get; init; }
}

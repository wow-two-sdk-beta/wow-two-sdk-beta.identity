namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

/// <summary>Represents the outcome of one refresh: its status and, once refreshed, the new tokens and claims.</summary>
internal sealed record BffTokenRefreshResult
{
    /// <summary>Holds the result of a refused grant.</summary>
    public static readonly BffTokenRefreshResult Rejected = new() { Status = BffTokenRefreshStatus.Rejected };

    /// <summary>Holds the result of an unreachable or failing instance.</summary>
    public static readonly BffTokenRefreshResult Unavailable = new() { Status = BffTokenRefreshStatus.Unavailable };

    /// <summary>Gets how the refresh ended.</summary>
    public required BffTokenRefreshStatus Status { get; init; }

    /// <summary>Gets the new access token.</summary>
    public string? AccessToken { get; init; }

    /// <summary>Gets the new access token's lifetime, or null when the response named none.</summary>
    public TimeSpan? AccessTokenLifetime { get; init; }

    /// <summary>Gets the rotated refresh token, or null when the instance kept the old one.</summary>
    public string? RefreshToken { get; init; }

    /// <summary>Gets the new ID token, or null when the response carried none.</summary>
    public string? IdentityToken { get; init; }

    /// <summary>Gets the person's claims from the validated new ID token, or null without one.</summary>
    public IReadOnlyList<BffSessionClaim>? Claims { get; init; }
}

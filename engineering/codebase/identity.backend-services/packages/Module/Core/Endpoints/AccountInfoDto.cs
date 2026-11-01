namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>The signed-in account as the manage endpoint returns it.</summary>
public sealed record AccountInfoDto
{
    /// <summary>The user id.</summary>
    public required string UserId { get; init; }

    /// <summary>The user name.</summary>
    public string? UserName { get; init; }

    /// <summary>The email address.</summary>
    public string? Email { get; init; }

    /// <summary>Whether the email is confirmed.</summary>
    public bool IsEmailConfirmed { get; init; }

    /// <summary>Whether two-factor authentication is enabled.</summary>
    public bool IsTwoFactorEnabled { get; init; }
}

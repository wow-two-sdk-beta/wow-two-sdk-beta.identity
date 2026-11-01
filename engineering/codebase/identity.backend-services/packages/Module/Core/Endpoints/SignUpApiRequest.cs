namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>Signs an account up with an email and password.</summary>
public sealed record SignUpApiRequest
{
    /// <summary>The account email.</summary>
    public required string Email { get; init; }

    /// <summary>The initial password.</summary>
    public required string Password { get; init; }

    /// <summary>Optional user name; the email when omitted.</summary>
    public string? UserName { get; init; }
}

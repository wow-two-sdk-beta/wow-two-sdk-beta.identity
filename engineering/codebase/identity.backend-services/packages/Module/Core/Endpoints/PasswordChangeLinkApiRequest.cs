namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>Changes a password with the token of the link that was sent to the person's verified channel.</summary>
public sealed record PasswordChangeLinkApiRequest
{
    /// <summary>The token from the link's fragment.</summary>
    public required string Token { get; init; }

    /// <summary>The replacement password.</summary>
    public required string NewPassword { get; init; }
}

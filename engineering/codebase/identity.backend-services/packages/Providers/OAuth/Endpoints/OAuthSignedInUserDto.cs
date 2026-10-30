namespace WoW.Two.Sdk.Beta.Be.Identity.OAuth.Endpoints;

/// <summary>The signed-in user as the <c>me</c> endpoint returns it, read from the normalized <c>wt:*</c> claims.</summary>
public sealed record OAuthSignedInUserDto
{
    /// <summary>The provider handle (<c>wt:username</c>), else the name claim; empty when neither is present.</summary>
    public required string Login { get; init; }

    /// <summary>The display name (<c>wt:display_name</c>), else the handle; empty when neither is present.</summary>
    public required string Name { get; init; }

    /// <summary>The avatar URL, when the provider gave one.</summary>
    public string? Avatar { get; init; }
}

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Endpoints;

/// <summary>Represents the signed-in person, from the instance's ID token.</summary>
public sealed record BffPersonDto
{
    /// <summary>Gets the person's id at the instance (<c>sub</c>).</summary>
    public required string Sub { get; init; }

    /// <summary>Gets the display name (<c>name</c>), or null without the <c>profile</c> scope.</summary>
    public required string? Name { get; init; }

    /// <summary>Gets the link to the person's picture (<c>picture</c>), or null for a person without one or without the <c>profile</c> scope.</summary>
    public string? Picture { get; init; }

    /// <summary>Gets the email address, or null for a person without one or without the <c>email</c> scope.</summary>
    public required string? Email { get; init; }

    /// <summary>Gets the phone number, or null for a person without one or without the <c>phone</c> scope.</summary>
    public required string? PhoneNumber { get; init; }
}

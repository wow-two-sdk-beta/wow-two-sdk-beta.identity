namespace Identity.Api.Console.Models;

/// <summary>Represents the person signed in to the console.</summary>
public sealed record ConsoleSessionDto
{
    /// <summary>Gets the person's id.</summary>
    public required string UserId { get; init; }

    /// <summary>Gets the name the console shows: the display name, else the phone, else the user name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the instance roles the person holds, in catalog order.</summary>
    public required IReadOnlyList<string> Roles { get; init; }
}

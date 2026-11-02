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

    /// <summary>Gets the organizations the person manages as an owner or admin, where the console makes invitations and manages members.</summary>
    public required IReadOnlyList<ConsoleOrganizationDto> Organizations { get; init; }
}

/// <summary>Represents an organization the console's person manages.</summary>
public sealed record ConsoleOrganizationDto
{
    /// <summary>Gets the organization's id.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the organization's display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the organization's handle.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the person's role there: <c>owner</c> or <c>admin</c>.</summary>
    public required string Role { get; init; }
}

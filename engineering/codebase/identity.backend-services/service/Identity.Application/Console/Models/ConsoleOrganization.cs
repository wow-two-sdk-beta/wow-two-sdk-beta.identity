namespace Identity.Application.Console.Models;

/// <summary>Describes an organization visible to a console operator.</summary>
public sealed record ConsoleOrganization(Guid Id, string Name, string Slug, string Role);

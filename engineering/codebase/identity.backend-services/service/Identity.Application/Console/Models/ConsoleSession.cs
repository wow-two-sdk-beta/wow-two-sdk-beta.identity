namespace Identity.Application.Console.Models;

/// <summary>Describes the caller and the organizations they manage.</summary>
public sealed record ConsoleSession(Guid UserId, string Name, IReadOnlyList<string> Roles, IReadOnlyList<ConsoleOrganization> Organizations);

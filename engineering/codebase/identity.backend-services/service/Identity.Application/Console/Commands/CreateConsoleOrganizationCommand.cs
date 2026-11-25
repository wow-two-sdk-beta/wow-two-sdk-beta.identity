using Identity.Application.Console.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Identity.Application.Console.Commands;

/// <summary>Creates an organization with the signed-in administrator as its owner.</summary>
public sealed record CreateConsoleOrganizationCommand(Guid? CallerId, Guid? OrganizationId, string? Name, string? Slug)
    : ICommand<AppResult<ConsoleOrganization>>;

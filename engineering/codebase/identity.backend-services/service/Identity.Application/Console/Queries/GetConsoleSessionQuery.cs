using Identity.Application.Console.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Identity.Application.Console.Queries;

/// <summary>Reads the current instance roles and managed organizations of the signed-in person.</summary>
public sealed record GetConsoleSessionQuery(Guid? CallerId) : IQuery<AppResult<ConsoleSession>>;

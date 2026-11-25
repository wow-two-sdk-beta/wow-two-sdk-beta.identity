using Identity.Application.Status.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Identity.Application.Status.Queries;

/// <summary>Reads the identity host's build status.</summary>
public sealed record GetSystemStatusQuery : IQuery<AppResult<IdentitySystemStatus>>;

using Identity.Application.AccountDeletion.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Identity.Application.AccountDeletion.Commands;

/// <summary>Applies the authenticated deletion client's product-specific account policy.</summary>
public sealed record DeleteServiceAccountCommand(string ClientId, Guid PersonId) : ICommand<AppResult<ServiceDeletionOutcome>>;

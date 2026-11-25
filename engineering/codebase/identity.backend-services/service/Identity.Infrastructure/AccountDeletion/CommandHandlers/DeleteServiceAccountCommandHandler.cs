using Identity.Application.AccountDeletion.Commands;
using Identity.Application.AccountDeletion.Models;
using Identity.Infrastructure.AccountDeletion.Services;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Identity.Infrastructure.AccountDeletion.CommandHandlers;

/// <inheritdoc />
public sealed class DeleteServiceAccountCommandHandler(ServiceAccountDeletionService deletion)
    : ICommandHandler<DeleteServiceAccountCommand, AppResult<ServiceDeletionOutcome>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<ServiceDeletionOutcome>> HandleAsync(DeleteServiceAccountCommand command, CancellationToken cancellationToken) =>
        AppResult<ServiceDeletionOutcome>.Ok(await deletion.DeleteAsync(command.ClientId, command.PersonId, cancellationToken));
}

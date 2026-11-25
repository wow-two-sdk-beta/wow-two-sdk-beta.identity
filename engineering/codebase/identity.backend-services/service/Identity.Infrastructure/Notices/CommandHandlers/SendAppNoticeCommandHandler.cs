using Identity.Application.Notices.Commands;
using WoW.Two.Sdk.Beta.Be.Identity.Notices;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Identity.Infrastructure.Notices.CommandHandlers;

/// <inheritdoc />
public sealed class SendAppNoticeCommandHandler(AppNoticeService notices)
    : ICommandHandler<SendAppNoticeCommand, AppResult<AppNoticeDelivery>>
{
    /// <inheritdoc />
    public async ValueTask<AppResult<AppNoticeDelivery>> HandleAsync(SendAppNoticeCommand command, CancellationToken cancellationToken)
    {
        var result = await notices.SendAsync(command.ClientId, command.Notice, cancellationToken);

        return result.Match(value => AppResult<AppNoticeDelivery>.Ok(value), error => AppResult<AppNoticeDelivery>.Fail(error));
    }
}

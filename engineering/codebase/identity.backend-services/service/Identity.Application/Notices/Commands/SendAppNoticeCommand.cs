using WoW.Two.Sdk.Beta.Be.Identity.Notices;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Identity.Application.Notices.Commands;

/// <summary>Delivers a notice on behalf of the authenticated app.</summary>
public sealed record SendAppNoticeCommand(string ClientId, AppNotice Notice) : ICommand<AppResult<AppNoticeDelivery>>;

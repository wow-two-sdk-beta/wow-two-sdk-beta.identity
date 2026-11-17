using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>
/// Integrates app notices in process (Form 1) through the instance's own Telegram bot: the host sends as
/// <see cref="AppNoticeOptions.LocalClientId"/>, under the same rules the service applies to each caller.
/// </summary>
/// <param name="notices">Sends the notices.</param>
/// <param name="options">The host's app name; <c>Identity:Notices</c> reloads live.</param>
public sealed class TelegramAppNoticeBroker(AppNoticeService notices, IOptionsMonitor<AppNoticeOptions> options) : IAppNoticeBroker
{
    /// <inheritdoc />
    public Task<Result<AppNoticeDelivery>> SendNoticeAsync(AppNotice notice, CancellationToken cancellationToken = default)
        => notices.SendAsync(options.CurrentValue.LocalClientId, notice, cancellationToken);
}

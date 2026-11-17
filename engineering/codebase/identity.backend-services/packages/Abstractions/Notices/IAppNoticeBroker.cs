using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>
/// Integrates Identity's app notices: an app's message to one person, sent through the instance's own Telegram bot to the
/// chat the person's confirmed phone is linked to. The app never talks to the bot; Form 1 sends in process, Forms 2 and 3
/// call <c>POST /api/identity/v1/notices</c> with a client-credentials token carrying <see cref="AppNoticeConstants.Scope"/>.
/// </summary>
/// <remarks>
///   - a success reads <see cref="AppNoticeStatusConstants.Sent"/>, <see cref="AppNoticeStatusConstants.Muted"/> (the person
///     muted the app's notices, nothing went out) or <see cref="AppNoticeStatusConstants.NotLinked"/> (no Telegram link)
///   - a failure carries its code (<see cref="AppNoticeErrorCodeConstants"/>) in the error's <c>identityCode</c> metadata
///     and, when a retry may pass, the seconds to wait in <c>retryAfter</c>; a caller records it as
///     <see cref="AppNoticeStatusConstants.Failed"/>
///   - one idempotency key sends at most once per app: a repeat answers the first outcome, a repeat with another body
///     fails with <see cref="AppNoticeErrorCodeConstants.IdempotencyKeyReused"/>
/// </remarks>
public interface IAppNoticeBroker
{
    /// <summary>Sends <paramref name="notice"/> to the person it names, unless they muted the app's notices.</summary>
    /// <param name="notice">The notice.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<Result<AppNoticeDelivery>> SendNoticeAsync(AppNotice notice, CancellationToken cancellationToken = default);
}

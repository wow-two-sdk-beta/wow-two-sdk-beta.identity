using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>
/// Handles the mute button of an app notice: the router has checked the presser is the person's own Telegram user and spent
/// the button, so the app's notices stop for the person (the payload names the app). The notice keeps its text and loses its
/// buttons; the toast says how to turn notices on again.
/// </summary>
/// <param name="store">Records the mute.</param>
/// <param name="messages">The bot's wording.</param>
/// <param name="time">The clock the mute is stamped with.</param>
public sealed class AppNoticeMuteHandler(IAppNoticeRepository store, TelegramBotMessages messages, TimeProvider time) : ITelegramCallbackHandler
{
    /// <inheritdoc />
    public async Task<TelegramCallbackResult> HandleAsync(TelegramCallback callback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (callback.UserId is not { } userId || string.IsNullOrWhiteSpace(callback.Payload))
            return TelegramCallbackResult.Done;

        await store.MuteAsync(userId, callback.Payload, time.GetUtcNow(), cancellationToken);
        return new TelegramCallbackResult { AnswerText = messages.Text(TelegramBotMessageKeyConstants.NoticesMuted, callback.Culture) };
    }
}

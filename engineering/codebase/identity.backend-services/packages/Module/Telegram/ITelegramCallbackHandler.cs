namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>
/// Defines what a press of a notice's button does, registered keyed by its action name
/// (<see cref="TelegramIdentityBuilderExtensions.AddTelegramCallbackHandler{THandler}"/>). The router has checked the
/// press already: the button is known and unused, and the presser is its allowed Telegram user; the button and its
/// siblings are spent before the handler runs.
/// </summary>
public interface ITelegramCallbackHandler
{
    /// <summary>Acts on the press.</summary>
    /// <param name="callback">The pressed button, its message and the presser.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The message's new text and the presser's toast; the buttons drop either way.</returns>
    Task<TelegramCallbackResult> HandleAsync(TelegramCallback callback, CancellationToken cancellationToken = default);
}

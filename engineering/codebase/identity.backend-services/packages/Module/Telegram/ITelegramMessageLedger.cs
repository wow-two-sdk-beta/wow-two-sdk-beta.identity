namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>
/// Defines persistence of the bot's message ledger: the codes and notices it sent, when each is cleaned up, and the inline
/// buttons they carry. Spending a button is conditional, so a press counts once.
/// </summary>
public interface ITelegramMessageLedger
{
    /// <summary>Records a sent message and its buttons.</summary>
    /// <param name="message">The message.</param>
    /// <param name="callbacks">Its buttons, each naming <paramref name="message"/>'s id; empty for none.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task AddAsync(IdentityTelegramMessage message, IReadOnlyCollection<IdentityTelegramCallback> callbacks, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the account's recorded messages of a kind in a chat due at <paramref name="at"/>, unless they are due already,
    /// and spends their unpressed buttons.
    /// </summary>
    /// <param name="botUserName">The bot's normalized user name.</param>
    /// <param name="chatId">The chat.</param>
    /// <param name="kind">The message kind.</param>
    /// <param name="userId">The account the messages are about.</param>
    /// <param name="at">The instant they become due.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>How many messages became due.</returns>
    Task<int> MakeDueAsync(string botUserName, long chatId, string kind, Guid userId, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>The bot's messages due at <paramref name="at"/>, longest due first.</summary>
    /// <param name="botUserName">The bot's normalized user name.</param>
    /// <param name="at">The instant dueness is judged at.</param>
    /// <param name="limit">The most messages to return.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<IReadOnlyList<IdentityTelegramMessage>> FindDueAsync(string botUserName, DateTimeOffset at, int limit, CancellationToken cancellationToken = default);

    /// <summary>Drops cleaned-up messages and their buttons.</summary>
    /// <param name="ids">The messages' <see cref="IdentityTelegramMessage.Id"/>s.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task RemoveAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>The button with this token digest and the message it is on, or null when either is gone.</summary>
    /// <param name="tokenHash">The hex SHA-256 of the button's callback data.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<TelegramCallbackRecord?> FindCallbackAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Spends a button, and with it every other unused button of its message.</summary>
    /// <param name="callbackId">The pressed button's id.</param>
    /// <param name="messageRecordId">The id of the message it is on.</param>
    /// <param name="at">The press instant.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Whether this call spent it; false when another press already did.</returns>
    Task<bool> TryUseCallbackAsync(Guid callbackId, Guid messageRecordId, DateTimeOffset at, CancellationToken cancellationToken = default);
}

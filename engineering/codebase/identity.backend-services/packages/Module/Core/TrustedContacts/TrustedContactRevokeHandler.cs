using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>
/// Handles the "Revoke trust" button of the notice a sign-in through a trusted contact sends the account's own chat: the
/// router has checked the presser is the account's own Telegram user and spent the button, so the contact goes and every
/// session started through them ends. The notice then reads "Trust revoked", with no button; a contact gone already reads
/// the same.
/// </summary>
/// <param name="contacts">Revokes the contact.</param>
/// <param name="accounts">Reads the account.</param>
/// <param name="messages">The bot's wording.</param>
public sealed class TrustedContactRevokeHandler(TrustedContactService contacts, UserAccountService accounts, TelegramBotMessages messages) : ITelegramCallbackHandler
{
    /// <inheritdoc />
    public async Task<TelegramCallbackResult> HandleAsync(TelegramCallback callback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (callback.UserId is not { } accountId
            || !Guid.TryParseExact(callback.Payload, "N", out var contactUserId)
            || await accounts.FindByIdAsync(accountId, cancellationToken) is not { } account)
        {
            return TelegramCallbackResult.Done;
        }

        await contacts.RevokeAsync(account, contactUserId, cancellationToken);
        var revoked = messages.Text(TelegramBotMessageKeyConstants.TrustRevoked, callback.Culture);
        return new TelegramCallbackResult { MessageText = revoked, AnswerText = revoked };
    }
}

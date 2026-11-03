using Telegram.Bot.Types.ReplyMarkups;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>Sets the account's own phone once an <c>add-phone</c> start link's contact proves a number, and tells the person how it went.</summary>
/// <param name="phones">Sets the phone.</param>
/// <param name="replies">Answers in the bot.</param>
public sealed class AddPhoneTelegramLinkHandler(OwnPhoneService phones, TelegramBotReplier replies) : ITelegramLinkCompletionHandler
{
    /// <inheritdoc />
    public string Purpose => TelegramLinkPurposeConstants.AddPhone;

    /// <inheritdoc />
    public string SharePromptKey => TelegramBotMessageKeyConstants.AddPhoneSharePrompt;

    /// <inheritdoc />
    public async Task HandleAsync(TelegramLinkCompletion completion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        var reply = completion.ReferenceId is { } referenceId
            ? await phones.CompleteAsync(referenceId, completion.PhoneNumber, cancellationToken)
            : TelegramBotMessageKeyConstants.AddPhoneFailed;
        await replies.ReplyAsync(completion.ChatId, reply, completion.Culture, new ReplyKeyboardRemove(), cancellationToken);
    }
}

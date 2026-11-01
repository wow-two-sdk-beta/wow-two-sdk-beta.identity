using Telegram.Bot.Types.ReplyMarkups;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;

/// <summary>Sends the pending sign-in code once a phone sign-in's start link completes, taking the share button away with it.</summary>
/// <param name="signIn">Creates and sends the code.</param>
/// <param name="replies">Answers when a code went out moments ago.</param>
public sealed class PhoneSignInTelegramLinkHandler(UserPhoneSignInService signIn, TelegramBotReplier replies) : ITelegramLinkCompletionHandler
{
    /// <inheritdoc />
    public string Purpose => TelegramLinkPurposeConstants.PhoneSignIn;

    /// <inheritdoc />
    public async Task HandleAsync(TelegramLinkCompletion completion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        var sent = await signIn.SendCodeAsync(completion.PhoneNumber, completion.ChatId, origin: null, completion.Culture, isKeyboardRemoved: true, cancellationToken);
        if (sent.Status == PhoneSignInStatus.RateLimited)
            await replies.ReplyAsync(completion.ChatId, TelegramBotMessageKeyConstants.CodeRecentlySent, completion.Culture, new ReplyKeyboardRemove(), cancellationToken);
    }
}

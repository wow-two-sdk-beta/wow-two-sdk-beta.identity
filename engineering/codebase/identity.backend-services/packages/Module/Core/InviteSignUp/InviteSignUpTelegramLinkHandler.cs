using Telegram.Bot.Types.ReplyMarkups;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.InviteSignUp;

/// <summary>Finishes an invitation sign-up once its start link's contact proves a number, and tells the person how it went.</summary>
/// <param name="signUp">Joins the person.</param>
/// <param name="replies">Answers in the bot.</param>
public sealed class InviteSignUpTelegramLinkHandler(InviteSignUpService signUp, TelegramBotReplier replies) : ITelegramLinkCompletionHandler
{
    /// <inheritdoc />
    public string Purpose => TelegramLinkPurposeConstants.InviteSignUp;

    /// <inheritdoc />
    public string SharePromptKey => TelegramBotMessageKeyConstants.SignUpSharePrompt;

    /// <inheritdoc />
    public string NumberMismatchKey => TelegramBotMessageKeyConstants.InvitationMismatch;

    /// <inheritdoc />
    public async Task HandleAsync(TelegramLinkCompletion completion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        var reply = completion.ReferenceId is { } signUpId
            ? await signUp.CompleteAsync(signUpId, completion.PhoneNumber, cancellationToken)
            : TelegramBotMessageKeyConstants.SignUpFailed;
        await replies.ReplyAsync(completion.ChatId, reply, completion.Culture, new ReplyKeyboardRemove(), cancellationToken);
    }
}

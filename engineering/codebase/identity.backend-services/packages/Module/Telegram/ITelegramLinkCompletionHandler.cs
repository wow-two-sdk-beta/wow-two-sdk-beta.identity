namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>Defines what follows a completed start link of one purpose, such as sending the pending sign-in code.</summary>
public interface ITelegramLinkCompletionHandler
{
    /// <summary>The purpose this handler completes, one of <see cref="TelegramLinkPurposeConstants"/>.</summary>
    string Purpose { get; }

    /// <summary>The text asking for the contact once the link opens (<see cref="TelegramBotMessageKeyConstants"/>).</summary>
    string SharePromptKey => TelegramBotMessageKeyConstants.SharePrompt;

    /// <summary>The text for a contact holding another number than the link's (<see cref="TelegramBotMessageKeyConstants"/>).</summary>
    string NumberMismatchKey => TelegramBotMessageKeyConstants.NumberMismatch;

    /// <summary>Acts on the completed link; the bot has replied to nothing yet, so this answers the person.</summary>
    /// <param name="completion">The linked number, chat and culture.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task HandleAsync(TelegramLinkCompletion completion, CancellationToken cancellationToken = default);
}

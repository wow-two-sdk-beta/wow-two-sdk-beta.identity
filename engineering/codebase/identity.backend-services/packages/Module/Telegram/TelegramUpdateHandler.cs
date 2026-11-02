using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>
/// Handles the bot's webhook updates from private chats. <c>/start {token}</c> attaches the chat to a pending start link
/// and asks for the person's own contact in the purpose's words; a shared contact whose user is the sender, holding the
/// link's number or, for a link that names none, any number, completes it once, saves the number's link to the chat and
/// hands over to the purpose's handler. Everything else is left unanswered.
/// </summary>
/// <param name="repository">The links and start links.</param>
/// <param name="replier">Sends the bot's texts.</param>
/// <param name="messages">The person's culture.</param>
/// <param name="handlers">What follows a completed link, per purpose.</param>
/// <param name="options">The bot; <c>Identity:Telegram</c> reloads live.</param>
/// <param name="time">The clock expiry is judged against.</param>
/// <param name="logger">Logs a completed link no handler serves.</param>
public sealed partial class TelegramUpdateHandler(
    ITelegramLinkRepository repository,
    TelegramBotReplier replier,
    TelegramBotMessages messages,
    IEnumerable<ITelegramLinkCompletionHandler> handlers,
    IOptionsMonitor<TelegramBotOptions> options,
    TimeProvider time,
    ILogger<TelegramUpdateHandler> logger)
{
    private const string StartCommand = "/start";

    /// <summary>Acts on one update.</summary>
    /// <param name="update">The update Telegram posted.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task HandleAsync(Update update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (update.Message is not { From: { IsBot: false } from, Chat: { Type: ChatType.Private } chat } message)
            return;

        var culture = messages.CultureOf(from.LanguageCode);
        if (message.Contact is { } contact)
            await OnContactAsync(contact, chat.Id, from.Id, culture, cancellationToken);
        else if (StartTokenOf(message.Text) is { } token)
            await OnStartAsync(token, chat.Id, from.Id, culture, cancellationToken);
    }

    /// <summary>The token of a <c>/start</c> command: empty for a bare one, null for any other text.</summary>
    private static string? StartTokenOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmed = text.Trim();
        var command = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var name = command[0].Split('@', 2)[0];
        return string.Equals(name, StartCommand, StringComparison.OrdinalIgnoreCase)
            ? (command.Length > 1 ? command[1].Trim() : string.Empty)
            : null;
    }

    private async Task OnStartAsync(string token, long chatId, long telegramUserId, CultureInfo culture, CancellationToken cancellationToken)
    {
        if (token.Length == 0)
        {
            await ReplyAsync(chatId, TelegramBotMessageKeyConstants.Welcome, culture, null, cancellationToken);
            return;
        }

        var request = await repository.FindPendingRequestAsync(BotUserName, TelegramLinkService.TokenHashOf(token), time.GetUtcNow(), cancellationToken);
        if (request is null)
        {
            await ReplyAsync(chatId, TelegramBotMessageKeyConstants.LinkExpired, culture, new ReplyKeyboardRemove(), cancellationToken);
            return;
        }

        await repository.AttachAsync(request.Id, chatId, telegramUserId, cancellationToken);
        var prompt = HandlerOf(request.Purpose)?.SharePromptKey ?? TelegramBotMessageKeyConstants.SharePrompt;
        await ReplyAsync(chatId, prompt, culture, ShareKeyboard(culture), cancellationToken);
    }

    private async Task OnContactAsync(Contact contact, long chatId, long telegramUserId, CultureInfo culture, CancellationToken cancellationToken)
    {
        // A forwarded contact card names someone else; only the sender's own contact proves the number.
        if (contact.UserId != telegramUserId)
        {
            await ReplyAsync(chatId, TelegramBotMessageKeyConstants.NotOwnContact, culture, ShareKeyboard(culture), cancellationToken);
            return;
        }

        var now = time.GetUtcNow();
        var request = await repository.FindOpenedRequestAsync(BotUserName, telegramUserId, now, cancellationToken);
        if (request is null)
        {
            await ReplyAsync(chatId, TelegramBotMessageKeyConstants.LinkExpired, culture, new ReplyKeyboardRemove(), cancellationToken);
            return;
        }

        // A link the contact names the number of takes the contact's; any other must hold the link's own.
        var handler = HandlerOf(request.Purpose);
        var phone = PhoneNumberPolicy.NormalizeContact(contact.PhoneNumber);
        if (phone is null || (request.PhoneNumber is not null && !string.Equals(phone, request.PhoneNumber, StringComparison.Ordinal)))
        {
            var mismatch = handler?.NumberMismatchKey ?? TelegramBotMessageKeyConstants.NumberMismatch;
            await ReplyAsync(chatId, mismatch, culture, new ReplyKeyboardRemove(), cancellationToken);
            return;
        }

        if (!await repository.TryConsumeRequestAsync(request.Id, now, cancellationToken))
            return;

        await repository.SaveLinkAsync(
            new IdentityTelegramLink
            {
                Id = Guid.CreateVersion7(now),
                BotUserName = BotUserName,
                PhoneNumber = phone,
                ChatId = chatId,
                TelegramUserId = telegramUserId,
                LinkedAt = now,
            },
            cancellationToken);

        var completion = new TelegramLinkCompletion
        {
            Purpose = request.Purpose,
            PhoneNumber = phone,
            ChatId = chatId,
            Culture = culture,
            TelegramUserId = telegramUserId,
            ReferenceId = request.ReferenceId,
        };
        if (handler is null)
        {
            LogNoHandler(logger, request.Purpose);
            return;
        }

        await handler.HandleAsync(completion, cancellationToken);
    }

    private ITelegramLinkCompletionHandler? HandlerOf(string purpose)
        => handlers.FirstOrDefault(h => string.Equals(h.Purpose, purpose, StringComparison.Ordinal));

    private Task ReplyAsync(long chatId, string key, CultureInfo culture, ReplyMarkup? keyboard, CancellationToken cancellationToken)
        => replier.ReplyAsync(chatId, key, culture, keyboard, cancellationToken);

    private ReplyKeyboardMarkup ShareKeyboard(CultureInfo culture) => replier.ShareKeyboard(culture);

    private string BotUserName => options.CurrentValue.NormalizedBotUserName;

    [LoggerMessage(Level = LogLevel.Warning, Message = "A Telegram start link for '{Purpose}' completed, but no handler serves that purpose.")]
    private static partial void LogNoHandler(ILogger logger, string purpose);
}

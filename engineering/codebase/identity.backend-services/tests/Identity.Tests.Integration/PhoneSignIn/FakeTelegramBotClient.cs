using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.PhoneSignIn;

/// <summary>
/// A bot client that keeps every message, deletion, edit and button answer instead of calling Telegram; chats in
/// <see cref="BlockedChats"/> refuse messages, as a blocked bot does.
/// </summary>
internal sealed partial class FakeTelegramBotClient : ITelegramBotClient
{
    private int _messageId;

    /// <summary>Every message the bot sent, in order.</summary>
    public ConcurrentQueue<SendMessageRequest> Sent { get; } = new();

    /// <summary>Every <c>deleteMessages</c> call, in order.</summary>
    public ConcurrentQueue<DeleteMessagesRequest> Deletions { get; } = new();

    /// <summary>Every <c>editMessageText</c> call, in order.</summary>
    public ConcurrentQueue<EditMessageTextRequest> TextEdits { get; } = new();

    /// <summary>Every <c>answerCallbackQuery</c> call, in order.</summary>
    public ConcurrentQueue<AnswerCallbackQueryRequest> Answers { get; } = new();

    /// <summary>Chats that refuse messages with Telegram's 403.</summary>
    public ConcurrentDictionary<long, bool> BlockedChats { get; } = new();

    /// <inheritdoc />
    public bool LocalBotServer => false;

    /// <inheritdoc />
    public long BotId => 123456;

    /// <inheritdoc />
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public IExceptionParser ExceptionsParser { get; set; } = new DefaultExceptionParser();

    /// <inheritdoc />
    public event AsyncEventHandler<ApiRequestEventArgs>? OnMakingApiRequest { add { } remove { } }

    /// <inheritdoc />
    public event AsyncEventHandler<ApiResponseEventArgs>? OnApiResponseReceived { add { } remove { } }

    /// <summary>The messages sent to <paramref name="chatId"/>.</summary>
    public IReadOnlyList<SendMessageRequest> To(long chatId) => [.. Sent.Where(m => m.ChatId.Identifier == chatId)];

    /// <summary>The last six-digit code sent to <paramref name="chatId"/>.</summary>
    public string LastCode(long chatId)
        => To(chatId).Select(m => CodePattern().Match(m.Text)).Last(match => match.Success).Value;

    /// <inheritdoc />
    public Task<TResponse> SendRequest<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        switch (request)
        {
            case DeleteMessagesRequest delete:
                Deletions.Enqueue(delete);
                return Task.FromResult((TResponse)(object)true);
            case EditMessageTextRequest edit:
                TextEdits.Enqueue(edit);
                object edited = new Message { Id = edit.MessageId, Date = DateTime.UtcNow, Chat = new Chat { Id = edit.ChatId.Identifier ?? 0, Type = ChatType.Private }, Text = edit.Text };
                return Task.FromResult((TResponse)edited);
            case AnswerCallbackQueryRequest answer:
                Answers.Enqueue(answer);
                return Task.FromResult((TResponse)(object)true);
            case not SendMessageRequest:
                throw new NotSupportedException($"The fake bot does not play {request.MethodName}.");
        }

        var message = (SendMessageRequest)request;

        var chatId = message.ChatId.Identifier ?? 0;
        if (BlockedChats.ContainsKey(chatId))
            throw new ApiRequestException("Forbidden: bot was blocked by the user", 403);

        Sent.Enqueue(message);
        object sent = new Message
        {
            Id = Interlocked.Increment(ref _messageId),
            Date = DateTime.UtcNow,
            Chat = new Chat { Id = chatId, Type = ChatType.Private },
            Text = message.Text,
        };
        return Task.FromResult((TResponse)sent);
    }

    /// <inheritdoc />
    public Task<bool> TestApi(CancellationToken cancellationToken = default) => Task.FromResult(true);

    /// <inheritdoc />
    public Task DownloadFile(string filePath, Stream destination, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task DownloadFile(TGFile file, Stream destination, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    [GeneratedRegex("[0-9]{6}")]
    private static partial Regex CodePattern();
}

using System.Collections.Concurrent;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Identity.Tests.Conformance;

/// <summary>A bot client that keeps every message it is asked to send instead of calling Telegram.</summary>
internal sealed class ConformanceTelegramBot : ITelegramBotClient
{
    private int _messageId;

    /// <summary>Every message sent, in order.</summary>
    public ConcurrentQueue<SendMessageRequest> Sent { get; } = new();

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

    /// <summary>How many messages went to <paramref name="chatId"/>.</summary>
    public int CountTo(long chatId) => Sent.Count(m => m.ChatId.Identifier == chatId);

    /// <inheritdoc />
    public Task<TResponse> SendRequest<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        if (request is not SendMessageRequest message)
            return request is AnswerCallbackQueryRequest or DeleteMessagesRequest or EditMessageReplyMarkupRequest
                ? Task.FromResult((TResponse)(object)true)
                : throw new NotSupportedException($"The conformance bot does not play {request.MethodName}.");

        Sent.Enqueue(message);
        object sent = new Message
        {
            Id = Interlocked.Increment(ref _messageId),
            Date = DateTime.UtcNow,
            Chat = new Chat { Id = message.ChatId.Identifier ?? 0, Type = ChatType.Private },
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
}

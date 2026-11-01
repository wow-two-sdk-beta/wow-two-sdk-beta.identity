using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TestMode;

/// <summary>
/// Takes the bot client's place in test mode: a message to a chat lands in the <see cref="TestMessageSink"/> and is
/// accepted, as if the person had read it. It sends messages only; anything else is a bug in a test-mode flow.
/// </summary>
/// <param name="sink">Keeps the messages.</param>
/// <param name="time">Stamps the replies Telegram would return.</param>
internal sealed class TestModeTelegramBotClient(TestMessageSink sink, TimeProvider time) : ITelegramBotClient
{
    private int _messageId;

    /// <inheritdoc />
    public bool LocalBotServer => false;

    /// <inheritdoc />
    public long BotId => 0;

    /// <inheritdoc />
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public IExceptionParser ExceptionsParser { get; set; } = new DefaultExceptionParser();

    /// <inheritdoc />
    public event AsyncEventHandler<ApiRequestEventArgs>? OnMakingApiRequest { add { } remove { } }

    /// <inheritdoc />
    public event AsyncEventHandler<ApiResponseEventArgs>? OnApiResponseReceived { add { } remove { } }

    /// <inheritdoc />
    public Task<TResponse> SendRequest<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request is not SendMessageRequest message)
            throw new NotSupportedException($"The test-mode bot sends messages only, not {request.MethodName}.");

        var address = message.ChatId.Identifier is { } chatId ? TestMessageSink.ChatAddress(chatId) : message.ChatId.Username ?? string.Empty;
        sink.Add(OtpChannelNameConstants.Telegram, address, subject: null, message.Text);
        object sent = new Message
        {
            Id = Interlocked.Increment(ref _messageId),
            Date = time.GetUtcNow().UtcDateTime,
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

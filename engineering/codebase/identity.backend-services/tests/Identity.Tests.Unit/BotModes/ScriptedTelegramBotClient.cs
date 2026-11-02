using System.Collections.Concurrent;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.BotModes;

/// <summary>
/// A bot client that plays Telegram for the update modes: it hands out the updates it is given as <c>getUpdates</c> does
/// (confirming those below the offset), fails polls on cue, reports and records the webhook, and keeps what the bot sends,
/// deletes, edits and answers, failing chats and edits on cue. Nothing leaves the process.
/// </summary>
internal sealed class ScriptedTelegramBotClient : ITelegramBotClient
{
    private readonly object _gate = new();
    private readonly List<Update> _pending = [];
    private int _messageId;

    /// <summary>Every message the bot sent, in order.</summary>
    public ConcurrentQueue<SendMessageRequest> Sent { get; } = new();

    /// <summary>Every <c>getUpdates</c> call, in order.</summary>
    public ConcurrentQueue<GetUpdatesRequest> Polls { get; } = new();

    /// <summary>Every <c>setWebhook</c> call, in order.</summary>
    public ConcurrentQueue<SetWebhookRequest> SetWebhooks { get; } = new();

    /// <summary>Every request's method name, in order.</summary>
    public ConcurrentQueue<string> Methods { get; } = new();

    /// <summary>Every <c>deleteMessages</c> call, in order.</summary>
    public ConcurrentQueue<DeleteMessagesRequest> Deletions { get; } = new();

    /// <summary>Every <c>editMessageText</c> call, in order.</summary>
    public ConcurrentQueue<EditMessageTextRequest> TextEdits { get; } = new();

    /// <summary>Every <c>editMessageReplyMarkup</c> call, in order.</summary>
    public ConcurrentQueue<EditMessageReplyMarkupRequest> MarkupEdits { get; } = new();

    /// <summary>Every <c>answerCallbackQuery</c> call, in order.</summary>
    public ConcurrentQueue<AnswerCallbackQueryRequest> Answers { get; } = new();

    /// <summary>Chats whose <c>sendMessage</c> and <c>deleteMessages</c> throw the given failure.</summary>
    public ConcurrentDictionary<long, Exception> ChatFailures { get; } = new();

    /// <summary>Messages, by chat and id, whose edits throw the given failure.</summary>
    public ConcurrentDictionary<(long ChatId, int MessageId), Exception> EditFailures { get; } = new();

    /// <summary>Failures the next polls throw, one each, before any poll succeeds.</summary>
    public ConcurrentQueue<Exception> PollFailures { get; } = new();

    /// <summary>What <c>getWebhookInfo</c> reports; <c>setWebhook</c> replaces it.</summary>
    public WebhookInfo Webhook { get; set; } = new() { Url = string.Empty };

    /// <summary>The failure <c>getWebhookInfo</c> throws, if any.</summary>
    public Exception? WebhookInfoFailure { get; set; }

    /// <summary>The failure <c>setWebhook</c> throws, if any.</summary>
    public Exception? SetWebhookFailure { get; set; }

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

    /// <summary>Queues an update for the next poll.</summary>
    /// <param name="update">The update; its id orders it.</param>
    public void Enqueue(Update update)
    {
        lock (_gate)
            _pending.Add(update);
    }

    /// <summary>The updates no poll has confirmed yet.</summary>
    public int PendingCount
    {
        get
        {
            lock (_gate)
                return _pending.Count;
        }
    }

    /// <inheritdoc />
    public async Task<TResponse> SendRequest<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        Methods.Enqueue(request.MethodName);
        object? response;
        switch (request)
        {
            case SendMessageRequest message:
                if (ChatFailures.TryGetValue(message.ChatId.Identifier ?? 0, out var refusal))
                    throw refusal;
                Sent.Enqueue(message);
                response = new Message
                {
                    Id = Interlocked.Increment(ref _messageId),
                    Date = DateTime.UtcNow,
                    Chat = new Chat { Id = message.ChatId.Identifier ?? 0, Type = ChatType.Private },
                    Text = message.Text,
                };
                break;
            case GetUpdatesRequest poll:
                Polls.Enqueue(poll);
                if (PollFailures.TryDequeue(out var failure))
                    throw failure;
                response = await TakeAsync(poll, cancellationToken);
                break;
            case GetWebhookInfoRequest:
                if (WebhookInfoFailure is { } infoFailure)
                    throw infoFailure;
                response = Webhook;
                break;
            case SetWebhookRequest set:
                if (SetWebhookFailure is { } setFailure)
                    throw setFailure;
                SetWebhooks.Enqueue(set);
                Webhook = new WebhookInfo { Url = set.Url, AllowedUpdates = set.AllowedUpdates?.ToArray() };
                response = true;
                break;
            case DeleteMessagesRequest delete:
                Deletions.Enqueue(delete);
                if (ChatFailures.TryGetValue(delete.ChatId.Identifier ?? 0, out var deleteFailure))
                    throw deleteFailure;
                response = true;
                break;
            case EditMessageTextRequest edit:
                TextEdits.Enqueue(edit);
                if (EditFailures.TryGetValue((edit.ChatId.Identifier ?? 0, edit.MessageId), out var editFailure))
                    throw editFailure;
                response = new Message { Id = edit.MessageId, Date = DateTime.UtcNow, Chat = new Chat { Id = edit.ChatId.Identifier ?? 0, Type = ChatType.Private }, Text = edit.Text };
                break;
            case EditMessageReplyMarkupRequest markup:
                MarkupEdits.Enqueue(markup);
                if (EditFailures.TryGetValue((markup.ChatId.Identifier ?? 0, markup.MessageId), out var markupFailure))
                    throw markupFailure;
                response = new Message { Id = markup.MessageId, Date = DateTime.UtcNow, Chat = new Chat { Id = markup.ChatId.Identifier ?? 0, Type = ChatType.Private } };
                break;
            case AnswerCallbackQueryRequest answer:
                Answers.Enqueue(answer);
                response = true;
                break;
            default:
                throw new NotSupportedException($"The scripted bot does not play {request.MethodName}.");
        }

        return (TResponse)response!;
    }

    /// <inheritdoc />
    public Task<bool> TestApi(CancellationToken cancellationToken = default) => Task.FromResult(true);

    /// <inheritdoc />
    public Task DownloadFile(string filePath, Stream destination, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task DownloadFile(TGFile file, Stream destination, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <summary>Confirms the updates below the poll's offset and returns the rest, or waits a moment and returns none, as a held poll would.</summary>
    private async Task<Update[]> TakeAsync(GetUpdatesRequest poll, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _pending.RemoveAll(update => update.Id < (poll.Offset ?? 0));
            if (_pending.Count > 0)
                return [.. _pending.OrderBy(update => update.Id).Take(poll.Limit ?? 100)];
        }

        await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken);
        return [];
    }
}

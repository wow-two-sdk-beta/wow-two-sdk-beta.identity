using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.BotModes;

/// <summary>Keeps the bot's message ledger and its buttons in memory, for tests that need no database.</summary>
internal sealed class InMemoryTelegramMessageLedger : ITelegramMessageLedger
{
    private readonly object _gate = new();
    private readonly List<IdentityTelegramMessage> _messages = [];
    private readonly List<IdentityTelegramCallback> _callbacks = [];

    /// <summary>Failures the next due lookups throw, one each, as a database outage would.</summary>
    public Queue<Exception> FindDueFailures { get; } = new();

    /// <summary>How many due lookups ran.</summary>
    public int FindDueCount { get; private set; }

    /// <summary>The recorded messages, as stored.</summary>
    public IReadOnlyList<IdentityTelegramMessage> Messages
    {
        get
        {
            lock (_gate)
                return [.. _messages];
        }
    }

    /// <summary>The recorded buttons, as stored.</summary>
    public IReadOnlyList<IdentityTelegramCallback> Callbacks
    {
        get
        {
            lock (_gate)
                return [.. _callbacks];
        }
    }

    /// <summary>Records a message directly, as an earlier send would have.</summary>
    public IdentityTelegramMessage Seed(long chatId, int messageId, DateTimeOffset sentAt, DateTimeOffset deleteAt, string kind = TelegramMessageKindConstants.Code, Guid? userId = null, string? culture = null, string botUserName = "ocharo_bot")
    {
        var message = new IdentityTelegramMessage
        {
            Id = Guid.CreateVersion7(),
            BotUserName = botUserName,
            ChatId = chatId,
            MessageId = messageId,
            Kind = kind,
            UserId = userId,
            Culture = culture,
            SentAt = sentAt,
            DeleteAt = deleteAt,
        };
        lock (_gate)
            _messages.Add(message);
        return message;
    }

    /// <inheritdoc />
    public Task AddAsync(IdentityTelegramMessage message, IReadOnlyCollection<IdentityTelegramCallback> callbacks, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _messages.Add(message);
            _callbacks.AddRange(callbacks);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> MakeDueAsync(string botUserName, long chatId, string kind, Guid userId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var older = _messages.Where(m => m.BotUserName == botUserName && m.ChatId == chatId && m.Kind == kind && m.UserId == userId && m.DeleteAt > at).ToList();
            foreach (var message in older)
            {
                message.DeleteAt = at;
                foreach (var callback in _callbacks.Where(c => c.MessageRecordId == message.Id && c.UsedAt is null))
                    callback.UsedAt = at;
            }
            return Task.FromResult(older.Count);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IdentityTelegramMessage>> FindDueAsync(string botUserName, DateTimeOffset at, int limit, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            FindDueCount++;
            if (FindDueFailures.TryDequeue(out var failure))
                throw failure;

            IReadOnlyList<IdentityTelegramMessage> due = [.. _messages.Where(m => m.BotUserName == botUserName && m.DeleteAt <= at).OrderBy(m => m.DeleteAt).Take(limit)];
            return Task.FromResult(due);
        }
    }

    /// <inheritdoc />
    public Task RemoveAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _callbacks.RemoveAll(c => ids.Contains(c.MessageRecordId));
            _messages.RemoveAll(m => ids.Contains(m.Id));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<TelegramCallbackRecord?> FindCallbackAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var callback = _callbacks.FirstOrDefault(c => c.TokenHash == tokenHash);
            var message = callback is null ? null : _messages.FirstOrDefault(m => m.Id == callback.MessageRecordId);
            return Task.FromResult(callback is null || message is null ? null : new TelegramCallbackRecord(Copy(callback), message));
        }
    }

    /// <inheritdoc />
    public Task<bool> TryUseCallbackAsync(Guid callbackId, Guid messageRecordId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var pressed = _callbacks.FirstOrDefault(c => c.Id == callbackId && c.UsedAt is null);
            if (pressed is null)
                return Task.FromResult(false);

            foreach (var callback in _callbacks.Where(c => c.MessageRecordId == messageRecordId && c.UsedAt is null))
                callback.UsedAt = at;
            return Task.FromResult(true);
        }
    }

    /// <summary>A detached copy, as a database read returns, so a later spend does not change what a caller read.</summary>
    private static IdentityTelegramCallback Copy(IdentityTelegramCallback callback) => new()
    {
        Id = callback.Id,
        MessageRecordId = callback.MessageRecordId,
        TokenHash = callback.TokenHash,
        Action = callback.Action,
        Payload = callback.Payload,
        AllowedTelegramUserId = callback.AllowedTelegramUserId,
        UsedAt = callback.UsedAt,
    };
}

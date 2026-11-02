using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.BotModes;

/// <summary>Keeps the bot's links, start links and pending link changes in memory, for tests that need no database.</summary>
internal sealed class InMemoryTelegramLinkRepository : ITelegramLinkRepository
{
    private readonly object _gate = new();
    private readonly List<IdentityTelegramLink> _links = [];
    private readonly List<IdentityTelegramLinkRequest> _requests = [];
    private readonly List<IdentityTelegramLinkChange> _changes = [];

    /// <summary>Whether looking up a pending start link throws, as a database outage would.</summary>
    public bool IsPendingLookupFailing { get; set; }

    /// <summary>The links saved so far.</summary>
    public IReadOnlyList<IdentityTelegramLink> Links
    {
        get
        {
            lock (_gate)
                return [.. _links];
        }
    }

    /// <summary>The pending link changes, as stored.</summary>
    public IReadOnlyList<IdentityTelegramLinkChange> Changes
    {
        get
        {
            lock (_gate)
                return [.. _changes];
        }
    }

    /// <summary>The start links made so far, as stored.</summary>
    public IReadOnlyList<IdentityTelegramLinkRequest> Requests
    {
        get
        {
            lock (_gate)
                return [.. _requests];
        }
    }

    /// <inheritdoc />
    public Task<IdentityTelegramLink?> FindLinkAsync(string botUserName, string phoneNumber, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return Task.FromResult(_links.FirstOrDefault(l => l.BotUserName == botUserName && l.PhoneNumber == phoneNumber));
    }

    /// <inheritdoc />
    public Task SaveLinkAsync(IdentityTelegramLink link, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _links.RemoveAll(l => l.BotUserName == link.BotUserName && l.PhoneNumber == link.PhoneNumber);
            _links.Add(link);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteLinksAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _links.RemoveAll(l => l.PhoneNumber == phoneNumber);
            _changes.RemoveAll(c => c.PhoneNumber == phoneNumber);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> HasRecentRequestAsync(string botUserName, string? phoneNumber, string? originHash, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_requests.Any(r => r.BotUserName == botUserName && r.CreatedAt >= since
                && ((phoneNumber is not null && r.PhoneNumber == phoneNumber) || (originHash is not null && r.OriginHash == originHash))));
        }
    }

    /// <inheritdoc />
    public Task AddRequestAsync(IdentityTelegramLinkRequest request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            _requests.Add(request);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IdentityTelegramLinkRequest?> FindPendingRequestAsync(string botUserName, string tokenHash, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (IsPendingLookupFailing)
            throw new InvalidOperationException("The database is down.");

        lock (_gate)
            return Task.FromResult(_requests.FirstOrDefault(r => r.BotUserName == botUserName && r.TokenHash == tokenHash && r.ConsumedAt is null && r.ExpiresAt > at));
    }

    /// <inheritdoc />
    public Task<IdentityTelegramLinkRequest?> FindOpenedRequestAsync(string botUserName, long telegramUserId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_requests
                .Where(r => r.BotUserName == botUserName && r.TelegramUserId == telegramUserId && r.ConsumedAt is null && r.ExpiresAt > at)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefault());
        }
    }

    /// <inheritdoc />
    public Task AttachAsync(Guid requestId, long chatId, long telegramUserId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_requests.FirstOrDefault(r => r.Id == requestId && r.ConsumedAt is null) is { } request)
            {
                request.ChatId = chatId;
                request.TelegramUserId = telegramUserId;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> TryConsumeRequestAsync(Guid requestId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_requests.FirstOrDefault(r => r.Id == requestId && r.ConsumedAt is null) is not { } request)
                return Task.FromResult(false);

            request.ConsumedAt = at;
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task SaveChangeAsync(IdentityTelegramLinkChange change, DateTimeOffset expiredBefore, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _changes.RemoveAll(c => c.RequestedAt < expiredBefore);
            if (_changes.FirstOrDefault(c => c.BotUserName == change.BotUserName && c.PhoneNumber == change.PhoneNumber) is { } existing)
            {
                existing.ChatId = change.ChatId;
                existing.TelegramUserId = change.TelegramUserId;
                existing.RequestedAt = change.RequestedAt;
            }
            else
            {
                _changes.Add(change);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IdentityTelegramLinkChange?> FindChangeAsync(string botUserName, string phoneNumber, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_changes
                .Where(c => c.BotUserName == botUserName && c.PhoneNumber == phoneNumber && c.RequestedAt >= since)
                .Select(Copy)
                .FirstOrDefault());
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlySet<string>> FindChangedNumbersAsync(string botUserName, IReadOnlyCollection<string> phoneNumbers, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlySet<string> changed = _changes
                .Where(c => c.BotUserName == botUserName && c.RequestedAt >= since && phoneNumbers.Contains(c.PhoneNumber))
                .Select(c => c.PhoneNumber)
                .ToHashSet(StringComparer.Ordinal);
            return Task.FromResult(changed);
        }
    }

    /// <inheritdoc />
    public Task<bool> TryDeleteChangeAsync(IdentityTelegramLinkChange change, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return Task.FromResult(_changes.RemoveAll(c => c.Id == change.Id && c.ChatId == change.ChatId && c.TelegramUserId == change.TelegramUserId) == 1);
    }

    private static IdentityTelegramLinkChange Copy(IdentityTelegramLinkChange change) => new()
    {
        Id = change.Id,
        BotUserName = change.BotUserName,
        PhoneNumber = change.PhoneNumber,
        ChatId = change.ChatId,
        TelegramUserId = change.TelegramUserId,
        RequestedAt = change.RequestedAt,
    };
}

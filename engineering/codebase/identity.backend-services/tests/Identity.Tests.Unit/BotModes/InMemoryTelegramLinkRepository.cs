using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.BotModes;

/// <summary>Keeps the bot's links and start links in memory, for tests that need no database.</summary>
internal sealed class InMemoryTelegramLinkRepository : ITelegramLinkRepository
{
    private readonly object _gate = new();
    private readonly List<IdentityTelegramLink> _links = [];
    private readonly List<IdentityTelegramLinkRequest> _requests = [];

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
            _links.RemoveAll(l => l.PhoneNumber == phoneNumber);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> HasRecentRequestAsync(string botUserName, string phoneNumber, string? originHash, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return Task.FromResult(_requests.Any(r => r.BotUserName == botUserName && r.CreatedAt >= since && (r.PhoneNumber == phoneNumber || (originHash is not null && r.OriginHash == originHash))));
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
}

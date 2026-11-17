using WoW.Two.Sdk.Beta.Be.Identity.Notices;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.BotModes;

/// <summary>Keeps app notice keys and mutes in memory, for tests that need no database.</summary>
internal sealed class InMemoryAppNoticeRepository : IAppNoticeRepository
{
    private readonly object _gate = new();
    private readonly List<IdentityAppNotice> _requests = [];
    private readonly List<IdentityNoticeMute> _mutes = [];

    /// <summary>The mutes, as stored.</summary>
    public IReadOnlyList<IdentityNoticeMute> Mutes
    {
        get
        {
            lock (_gate)
                return [.. _mutes];
        }
    }

    /// <inheritdoc />
    public Task<AppNoticeClaim> ClaimAsync(IdentityAppNotice request, DateTimeOffset staleBefore, DateTimeOffset expiredBefore, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _requests.RemoveAll(r => r.CreatedAt < expiredBefore);
            var existing = _requests.FirstOrDefault(r => r.ClientId == request.ClientId && r.IdempotencyKey == request.IdempotencyKey);
            if (existing is null)
            {
                _requests.Add(request);
                return Task.FromResult(new AppNoticeClaim(request.Id, null));
            }

            if (existing.Status == IdentityAppNotice.PendingStatus && existing.CreatedAt < staleBefore && existing.RequestHash == request.RequestHash)
            {
                existing.CreatedAt = request.CreatedAt;
                return Task.FromResult(new AppNoticeClaim(existing.Id, null));
            }

            return Task.FromResult(new AppNoticeClaim(null, existing));
        }
    }

    /// <inheritdoc />
    public Task CompleteAsync(Guid id, string status, Guid? messageRecordId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            foreach (var request in _requests.Where(r => r.Id == id))
            {
                request.Status = status;
                request.MessageRecordId = messageRecordId;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ReleaseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            _requests.RemoveAll(r => r.Id == id && r.Status == IdentityAppNotice.PendingStatus);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> IsMutedAsync(Guid userId, string clientId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return Task.FromResult(_mutes.Any(m => m.UserId == userId && (m.ClientId == clientId || m.ClientId == IdentityNoticeMute.AllClients)));
    }

    /// <inheritdoc />
    public Task MuteAsync(Guid userId, string clientId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_mutes.Any(m => m.UserId == userId && m.ClientId == clientId))
                _mutes.Add(new IdentityNoticeMute { UserId = userId, ClientId = clientId, MutedAt = at });
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> UnmuteAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return Task.FromResult(_mutes.RemoveAll(m => userIds.Contains(m.UserId)));
    }

    /// <inheritdoc />
    public Task DeleteAccountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _mutes.RemoveAll(m => m.UserId == userId);
            _requests.RemoveAll(r => r.UserId == userId);
        }

        return Task.CompletedTask;
    }
}

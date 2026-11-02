using Microsoft.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Identity.Core;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>Accesses <c>identity_telegram_links</c> and <c>identity_telegram_link_requests</c> through <see cref="IdentityDbContext"/>.</summary>
/// <param name="context">The identity context.</param>
public sealed class EfTelegramLinkRepository(IdentityDbContext context) : ITelegramLinkRepository
{
    private static readonly TimeSpan PruneGrace = TimeSpan.FromDays(1);

    private DbSet<IdentityTelegramLink> Links => context.Set<IdentityTelegramLink>();

    private DbSet<IdentityTelegramLinkRequest> Requests => context.Set<IdentityTelegramLinkRequest>();

    /// <inheritdoc />
    public Task<IdentityTelegramLink?> FindLinkAsync(string botUserName, string phoneNumber, CancellationToken cancellationToken = default)
        => Links.AsNoTracking().FirstOrDefaultAsync(l => l.BotUserName == botUserName && l.PhoneNumber == phoneNumber, cancellationToken);

    /// <inheritdoc />
    public Task SaveLinkAsync(IdentityTelegramLink link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        return SaveLinkAsync(link, isRetry: false, cancellationToken);
    }

    private async Task SaveLinkAsync(IdentityTelegramLink link, bool isRetry, CancellationToken cancellationToken)
    {
        var moved = await Links
            .Where(l => l.BotUserName == link.BotUserName && l.PhoneNumber == link.PhoneNumber)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(l => l.ChatId, link.ChatId)
                    .SetProperty(l => l.TelegramUserId, link.TelegramUserId)
                    .SetProperty(l => l.LinkedAt, link.LinkedAt),
                cancellationToken);
        if (moved > 0)
            return;

        Links.Add(link);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (!isRetry)
        {
            // Another update linked the same number first; this chat proved it as well, so it takes the link.
            context.Entry(link).State = EntityState.Detached;
            await SaveLinkAsync(link, isRetry: true, cancellationToken);
            return;
        }

        context.Entry(link).State = EntityState.Detached;
    }

    /// <inheritdoc />
    public Task DeleteLinksAsync(string phoneNumber, CancellationToken cancellationToken = default)
        => Links.Where(l => l.PhoneNumber == phoneNumber).ExecuteDeleteAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> HasRecentRequestAsync(string botUserName, string? phoneNumber, string? originHash, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        var recent = Requests.AsNoTracking().Where(r => r.BotUserName == botUserName && r.CreatedAt >= since);
        return (phoneNumber, originHash) switch
        {
            (null, null) => Task.FromResult(false),
            (null, _) => recent.AnyAsync(r => r.OriginHash == originHash, cancellationToken),
            (_, null) => recent.AnyAsync(r => r.PhoneNumber == phoneNumber, cancellationToken),
            _ => recent.AnyAsync(r => r.PhoneNumber == phoneNumber || r.OriginHash == originHash, cancellationToken),
        };
    }

    /// <inheritdoc />
    public async Task AddRequestAsync(IdentityTelegramLinkRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var horizon = request.CreatedAt - PruneGrace;
        await Requests.Where(r => r.ExpiresAt < horizon).ExecuteDeleteAsync(cancellationToken);

        Requests.Add(request);
        await context.SaveChangesAsync(cancellationToken);
        context.Entry(request).State = EntityState.Detached;
    }

    /// <inheritdoc />
    public Task<IdentityTelegramLinkRequest?> FindPendingRequestAsync(string botUserName, string tokenHash, DateTimeOffset at, CancellationToken cancellationToken = default)
        => Requests.AsNoTracking().FirstOrDefaultAsync(
            r => r.BotUserName == botUserName && r.TokenHash == tokenHash && r.ConsumedAt == null && r.ExpiresAt > at,
            cancellationToken);

    /// <inheritdoc />
    public Task<IdentityTelegramLinkRequest?> FindOpenedRequestAsync(string botUserName, long telegramUserId, DateTimeOffset at, CancellationToken cancellationToken = default)
        => Requests.AsNoTracking()
            .Where(r => r.BotUserName == botUserName && r.TelegramUserId == telegramUserId && r.ConsumedAt == null && r.ExpiresAt > at)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task AttachAsync(Guid requestId, long chatId, long telegramUserId, CancellationToken cancellationToken = default)
        => Requests
            .Where(r => r.Id == requestId && r.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.ChatId, chatId).SetProperty(r => r.TelegramUserId, telegramUserId), cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TryConsumeRequestAsync(Guid requestId, DateTimeOffset at, CancellationToken cancellationToken = default)
        => await Requests
            .Where(r => r.Id == requestId && r.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.ConsumedAt, at), cancellationToken) == 1;
}

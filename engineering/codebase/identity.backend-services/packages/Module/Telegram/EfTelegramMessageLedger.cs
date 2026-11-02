using Microsoft.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Identity.Core;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>Accesses <c>identity_telegram_messages</c> and <c>identity_telegram_callbacks</c> through <see cref="IdentityDbContext"/>.</summary>
/// <param name="context">The identity context.</param>
public sealed class EfTelegramMessageLedger(IdentityDbContext context) : ITelegramMessageLedger
{
    private DbSet<IdentityTelegramMessage> Messages => context.Set<IdentityTelegramMessage>();

    private DbSet<IdentityTelegramCallback> Callbacks => context.Set<IdentityTelegramCallback>();

    /// <inheritdoc />
    public async Task AddAsync(IdentityTelegramMessage message, IReadOnlyCollection<IdentityTelegramCallback> callbacks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(callbacks);
        Messages.Add(message);
        Callbacks.AddRange(callbacks);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            // Saved or not, the rows leave the context, so a failed write never rides along with the scope's next save.
            context.Entry(message).State = EntityState.Detached;
            foreach (var callback in callbacks)
                context.Entry(callback).State = EntityState.Detached;
        }
    }

    /// <inheritdoc />
    public async Task<int> MakeDueAsync(string botUserName, long chatId, string kind, Guid userId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        var older = Messages.Where(m => m.UserId == userId && m.Kind == kind && m.BotUserName == botUserName && m.ChatId == chatId && m.DeleteAt > at);

        // A replaced notice's buttons stop working now, not at the sweep that deletes it.
        await Callbacks
            .Where(c => c.UsedAt == null && older.Any(m => m.Id == c.MessageRecordId))
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.UsedAt, at), cancellationToken);
        return await older.ExecuteUpdateAsync(setters => setters.SetProperty(m => m.DeleteAt, at), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IdentityTelegramMessage>> FindDueAsync(string botUserName, DateTimeOffset at, int limit, CancellationToken cancellationToken = default)
        => await Messages.AsNoTracking()
            .Where(m => m.BotUserName == botUserName && m.DeleteAt <= at)
            .OrderBy(m => m.DeleteAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task RemoveAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
            return;

        await Callbacks.Where(c => ids.Contains(c.MessageRecordId)).ExecuteDeleteAsync(cancellationToken);
        await Messages.Where(m => ids.Contains(m.Id)).ExecuteDeleteAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TelegramCallbackRecord?> FindCallbackAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        var callback = await Callbacks.AsNoTracking().FirstOrDefaultAsync(c => c.TokenHash == tokenHash, cancellationToken);
        if (callback is null)
            return null;

        var message = await Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == callback.MessageRecordId, cancellationToken);
        return message is null ? null : new TelegramCallbackRecord(callback, message);
    }

    /// <inheritdoc />
    public async Task<bool> TryUseCallbackAsync(Guid callbackId, Guid messageRecordId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        var spent = await Callbacks
            .Where(c => c.Id == callbackId && c.UsedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.UsedAt, at), cancellationToken);
        if (spent != 1)
            return false;

        await Callbacks
            .Where(c => c.MessageRecordId == messageRecordId && c.UsedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.UsedAt, at), cancellationToken);
        return true;
    }
}

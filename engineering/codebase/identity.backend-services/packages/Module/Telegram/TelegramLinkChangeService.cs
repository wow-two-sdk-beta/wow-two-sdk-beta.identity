using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>
/// Provides the pending changes of a number's link on the current bot. A number a person holds confirmed stays linked to
/// the Telegram account it was linked from: another account sharing it, as a recycled number's new holder would, leaves a
/// change that waits up to <see cref="Lifetime"/> for an owner or admin of the person's organization. Approving moves the
/// link to the chat that asked and touches nothing else; denying drops the change.
/// </summary>
/// <param name="repository">The links and their pending changes.</param>
/// <param name="options">The bot; <c>Identity:Telegram</c> reloads live.</param>
/// <param name="time">The clock changes are judged and links stamped against.</param>
public sealed class TelegramLinkChangeService(ITelegramLinkRepository repository, IOptionsMonitor<TelegramBotOptions> options, TimeProvider time)
{
    /// <summary>How long a pending change waits for approval; older ones no longer count and are pruned.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);

    /// <summary>The numbers among <paramref name="phoneNumbers"/> with a pending change on the current bot.</summary>
    /// <param name="phoneNumbers">The numbers in E.164.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public Task<IReadOnlySet<string>> FindPendingNumbersAsync(IReadOnlyCollection<string> phoneNumbers, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(phoneNumbers);
        return repository.FindChangedNumbersAsync(BotUserName, phoneNumbers, time.GetUtcNow() - Lifetime, cancellationToken);
    }

    /// <summary>Whether <paramref name="phoneNumber"/> has a pending change on the current bot.</summary>
    /// <param name="phoneNumber">The number in E.164.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task<bool> IsPendingAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        return await repository.FindChangeAsync(BotUserName, phoneNumber, time.GetUtcNow() - Lifetime, cancellationToken) is not null;
    }

    /// <summary>Moves the number's link to the chat and Telegram user its pending change names, and drops the change.</summary>
    /// <param name="phoneNumber">The number in E.164.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Whether a pending change was approved; false when none is pending.</returns>
    public async Task<bool> ApproveAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        var now = time.GetUtcNow();
        var change = await repository.FindChangeAsync(BotUserName, phoneNumber, now - Lifetime, cancellationToken);
        if (change is null || !await repository.TryDeleteChangeAsync(change, cancellationToken))
            return false;

        await repository.SaveLinkAsync(
            new IdentityTelegramLink
            {
                Id = Guid.CreateVersion7(now),
                BotUserName = change.BotUserName,
                PhoneNumber = change.PhoneNumber,
                ChatId = change.ChatId,
                TelegramUserId = change.TelegramUserId,
                LinkedAt = now,
            },
            cancellationToken);
        return true;
    }

    /// <summary>Drops the number's pending change; the link stays where it is.</summary>
    /// <param name="phoneNumber">The number in E.164.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Whether a pending change was denied; false when none is pending.</returns>
    public async Task<bool> DenyAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        var change = await repository.FindChangeAsync(BotUserName, phoneNumber, time.GetUtcNow() - Lifetime, cancellationToken);
        return change is not null && await repository.TryDeleteChangeAsync(change, cancellationToken);
    }

    private string BotUserName => options.CurrentValue.NormalizedBotUserName;
}

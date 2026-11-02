using WoW.Two.Sdk.Beta.Be.Identity.Core;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>
/// Defines persistence of the bot's phone links, pending start links and pending link changes; completing a start link
/// and resolving a change are conditional, so each happens once.
/// </summary>
public interface ITelegramLinkRepository
{
    /// <summary>The link of <paramref name="phoneNumber"/> to a chat of <paramref name="botUserName"/>, or null.</summary>
    /// <param name="botUserName">The bot's normalized user name.</param>
    /// <param name="phoneNumber">The number in E.164.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<IdentityTelegramLink?> FindLinkAsync(string botUserName, string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Links the number to the chat, moving an existing link of the same bot and number to it.</summary>
    /// <param name="link">The link; its id is kept only when the number had none.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SaveLinkAsync(IdentityTelegramLink link, CancellationToken cancellationToken = default);

    /// <summary>Removes every bot's link of <paramref name="phoneNumber"/>, and every pending change of it.</summary>
    /// <param name="phoneNumber">The number in E.164.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task DeleteLinksAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Whether a start link of the bot was made since <paramref name="since"/> for the number or from the origin.</summary>
    /// <param name="botUserName">The bot's normalized user name.</param>
    /// <param name="phoneNumber">The number in E.164; null checks the origin only.</param>
    /// <param name="originHash">The origin's digest; null checks the number only.</param>
    /// <param name="since">The window's start.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<bool> HasRecentRequestAsync(string botUserName, string? phoneNumber, string? originHash, DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>Saves a new start link, pruning those a day past their expiry.</summary>
    /// <param name="request">The start link.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task AddRequestAsync(IdentityTelegramLinkRequest request, CancellationToken cancellationToken = default);

    /// <summary>The pending, unexpired start link of the bot with this token digest, or null.</summary>
    /// <param name="botUserName">The bot's normalized user name.</param>
    /// <param name="tokenHash">The token's hex SHA-256.</param>
    /// <param name="at">The instant expiry is judged at.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<IdentityTelegramLinkRequest?> FindPendingRequestAsync(string botUserName, string tokenHash, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>The newest pending, unexpired start link of the bot that <paramref name="telegramUserId"/> opened, or null.</summary>
    /// <param name="botUserName">The bot's normalized user name.</param>
    /// <param name="telegramUserId">The Telegram user who opened it.</param>
    /// <param name="at">The instant expiry is judged at.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<IdentityTelegramLinkRequest?> FindOpenedRequestAsync(string botUserName, long telegramUserId, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>Records the chat and user that opened a pending start link.</summary>
    /// <param name="requestId">The start link's id.</param>
    /// <param name="chatId">The private chat.</param>
    /// <param name="telegramUserId">The Telegram user.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task AttachAsync(Guid requestId, long chatId, long telegramUserId, CancellationToken cancellationToken = default);

    /// <summary>Completes a pending start link.</summary>
    /// <param name="requestId">The start link's id.</param>
    /// <param name="at">The completion instant.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Whether this call completed it; false when another update already did.</returns>
    Task<bool> TryConsumeRequestAsync(Guid requestId, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a pending change of the number's link, replacing an earlier one of the same bot and number, and prunes
    /// changes requested before <paramref name="expiredBefore"/>.
    /// </summary>
    /// <param name="change">The change; its id is kept only when the number had none pending.</param>
    /// <param name="expiredBefore">Changes requested before this instant are gone.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SaveChangeAsync(IdentityTelegramLinkChange change, DateTimeOffset expiredBefore, CancellationToken cancellationToken = default);

    /// <summary>The pending change of the number's link on the bot, requested since <paramref name="since"/>, or null.</summary>
    /// <param name="botUserName">The bot's normalized user name.</param>
    /// <param name="phoneNumber">The number in E.164.</param>
    /// <param name="since">Changes requested before this instant no longer count.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<IdentityTelegramLinkChange?> FindChangeAsync(string botUserName, string phoneNumber, DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>The numbers among <paramref name="phoneNumbers"/> with a pending change on the bot, requested since <paramref name="since"/>.</summary>
    /// <param name="botUserName">The bot's normalized user name.</param>
    /// <param name="phoneNumbers">The numbers in E.164.</param>
    /// <param name="since">Changes requested before this instant no longer count.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<IReadOnlySet<string>> FindChangedNumbersAsync(string botUserName, IReadOnlyCollection<string> phoneNumbers, DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>Removes a pending change while it still names the same chat and Telegram user.</summary>
    /// <param name="change">The change as read.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Whether this call removed it; false when another call already did, or a newer request replaced it.</returns>
    Task<bool> TryDeleteChangeAsync(IdentityTelegramLinkChange change, CancellationToken cancellationToken = default);
}

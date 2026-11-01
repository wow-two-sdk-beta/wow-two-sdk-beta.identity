using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>Removes the bot's links of a deleted account's phone number, so its chat gets no more codes.</summary>
/// <param name="repository">The links.</param>
public sealed class TelegramLinkDeletionHandler(ITelegramLinkRepository repository) : IAccountDeletionHandler
{
    /// <inheritdoc />
    public async Task HandleAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!string.IsNullOrWhiteSpace(user.PhoneNumber))
            await repository.DeleteLinksAsync(user.PhoneNumber, cancellationToken);
    }
}

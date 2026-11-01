using System.Globalization;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Channels;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>
/// Offers the Telegram chat a person's confirmed phone number is linked to, on the instance's current bot, as a
/// <see cref="OtpChannelNameConstants.Telegram"/> channel, so a flow that must reach the person (a password-change code
/// or link) can use Telegram when it is their way in. It lists nothing while the bot is off, for an unconfirmed or
/// missing number, or for a number with no link.
/// </summary>
/// <param name="links">Finds the chat a number is linked to.</param>
public sealed class TelegramVerifiedChannelSource(TelegramLinkService links) : IUserVerifiedChannelSource
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<VerifiedChannel>> ListAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!links.IsEnabled || !user.IsPhoneNumberConfirmed || PhoneNumberPolicy.Normalize(user.PhoneNumber) is not { } phone)
            return [];

        return await links.FindChatAsync(phone, cancellationToken) is { } chatId
            ? [new VerifiedChannel { Channel = OtpChannelNameConstants.Telegram, Address = chatId.ToString(CultureInfo.InvariantCulture) }]
            : [];
    }
}

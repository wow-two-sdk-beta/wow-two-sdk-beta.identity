using WoW.Two.Sdk.Beta.Be.Identity.Core.Channels;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Emails;

/// <summary>Offers a person's confirmed email address as an <see cref="OtpChannelNameConstants.Email"/> channel.</summary>
public sealed class EmailVerifiedChannelSource : IUserVerifiedChannelSource
{
    /// <inheritdoc />
    public Task<IReadOnlyList<VerifiedChannel>> ListAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        IReadOnlyList<VerifiedChannel> channels = user.IsEmailConfirmed && !string.IsNullOrWhiteSpace(user.Email)
            ? [new VerifiedChannel { Channel = OtpChannelNameConstants.Email, Address = user.Email }]
            : [];
        return Task.FromResult(channels);
    }
}

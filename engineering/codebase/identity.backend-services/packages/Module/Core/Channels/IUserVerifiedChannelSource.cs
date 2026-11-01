namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Channels;

/// <summary>
/// Defines where a person can be reached with a one-time code or link: each slice that verifies a way to reach them
/// (a confirmed email, a linked Telegram chat) registers one source, so a flow that must reach the person names no
/// channel itself.
/// </summary>
public interface IUserVerifiedChannelSource
{
    /// <summary>The channels <paramref name="user"/> has verified, most preferred first; empty when this source has none for them.</summary>
    /// <param name="user">The person.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<VerifiedChannel>> ListAsync(IdentityUser user, CancellationToken cancellationToken = default);
}

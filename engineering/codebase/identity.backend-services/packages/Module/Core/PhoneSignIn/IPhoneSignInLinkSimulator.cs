using System.Globalization;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;

/// <summary>
/// Defines a stand-in for the person who opens a start link in Telegram and shares their contact, so a run with no real bot
/// can still complete the link. <see cref="UserPhoneSignInService"/> offers it every new start link; the only
/// implementation is test mode's (<c>Identity:TestMode</c>), and it answers false while that is off.
/// </summary>
public interface IPhoneSignInLinkSimulator
{
    /// <summary>Completes the start link as the person would, which sends the pending sign-in code; false when this simulator is not active.</summary>
    /// <param name="phoneNumber">The number in E.164 the link was made for.</param>
    /// <param name="startLinkUrl">The <c>t.me</c> start link just made.</param>
    /// <param name="culture">The culture of the person's Telegram, for the wording of the bot's replies.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Whether the link was completed and the code sent.</returns>
    Task<bool> TryCompleteAsync(string phoneNumber, string startLinkUrl, CultureInfo culture, CancellationToken cancellationToken = default);
}

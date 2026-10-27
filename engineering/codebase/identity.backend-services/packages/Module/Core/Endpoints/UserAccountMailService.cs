using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Comms.Email;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>
/// Provides the account emails — confirmation and password reset — composed from <see cref="UserAccountEmailSettings"/>
/// and sent through the registered <see cref="IEmailBroker"/>. Without a broker or a link template nothing is sent.
/// </summary>
/// <param name="settings">The live email settings.</param>
/// <param name="logger">Receives skipped and failed sends.</param>
/// <param name="email">The email provider; null sends nothing.</param>
public sealed class UserAccountMailService(
    IOptionsMonitor<UserAccountEmailSettings> settings,
    ILogger<UserAccountMailService> logger,
    IEmailBroker? email = null)
{
    /// <summary>Send the confirmation email carrying <paramref name="token"/>; returns whether the provider accepted it.</summary>
    /// <param name="user">The recipient account.</param>
    /// <param name="token">The confirmation token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<bool> SendConfirmationAsync(IdentityUser user, string token, CancellationToken cancellationToken = default)
    {
        var current = settings.CurrentValue;
        return SendAsync(user, token, current.ConfirmationLink, current.ConfirmationSubject, current.ConfirmationBody, cancellationToken);
    }

    /// <summary>Send the password-reset email carrying <paramref name="token"/>; returns whether the provider accepted it.</summary>
    /// <param name="user">The recipient account.</param>
    /// <param name="token">The reset token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<bool> SendPasswordResetAsync(IdentityUser user, string token, CancellationToken cancellationToken = default)
    {
        var current = settings.CurrentValue;
        return SendAsync(user, token, current.ResetLink, current.ResetSubject, current.ResetBody, cancellationToken);
    }

    /// <summary>Send the sign-in link carrying <paramref name="token"/>; nothing is sent without <c>MagicLink</c>.</summary>
    /// <param name="user">The recipient account.</param>
    /// <param name="token">The single-use link token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<bool> SendMagicLinkAsync(IdentityUser user, string token, CancellationToken cancellationToken = default)
    {
        var current = settings.CurrentValue;
        return SendAsync(user, token, current.MagicLink, current.MagicLinkSubject, current.MagicLinkBody, cancellationToken);
    }

    /// <summary>Send the sign-in <paramref name="code"/>; returns whether the provider accepted it.</summary>
    /// <param name="user">The recipient account.</param>
    /// <param name="code">The single-use code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> SendSignInCodeAsync(IdentityUser user, string code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (email is null || string.IsNullOrWhiteSpace(user.Email))
        {
            logger.AccountEmailSkipped();
            return false;
        }

        var current = settings.CurrentValue;
        var result = await email.SendAsync(EmailMessage.Create(user.Email, current.SignInCodeSubject, textBody: current.SignInCodeBody.Replace("{code}", code, StringComparison.Ordinal)), cancellationToken);
        if (!result.Success)
            logger.AccountEmailFailed(result.FailureReason);

        return result.Success;
    }

    private async Task<bool> SendAsync(IdentityUser user, string token, string? linkTemplate, string subject, string body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (email is null || string.IsNullOrWhiteSpace(linkTemplate) || string.IsNullOrWhiteSpace(user.Email))
        {
            logger.AccountEmailSkipped();
            return false;
        }

        var link = linkTemplate
            .Replace("{userId}", Uri.EscapeDataString(user.Id.ToString()), StringComparison.Ordinal)
            .Replace("{email}", Uri.EscapeDataString(user.Email), StringComparison.Ordinal)
            .Replace("{token}", Uri.EscapeDataString(token), StringComparison.Ordinal);
        var result = await email.SendAsync(EmailMessage.Create(user.Email, subject, textBody: body.Replace("{link}", link, StringComparison.Ordinal)), cancellationToken);
        if (!result.Success)
            logger.AccountEmailFailed(result.FailureReason);

        return result.Success;
    }
}

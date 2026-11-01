using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Comms.Email;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Emails;

/// <summary>Stands in for the email broker while no SMTP relay is configured: every send fails with a logged reason, so nothing else breaks.</summary>
/// <param name="logger">Receives a warning per skipped send.</param>
internal sealed class UnconfiguredEmailBroker(ILogger<UnconfiguredEmailBroker> logger) : IEmailBroker
{
    /// <summary>The failure reason a skipped send reports.</summary>
    internal const string FailureReason = "smtp_not_configured";

    /// <inheritdoc />
    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.SmtpNotConfigured();
        return Task.FromResult(new EmailSendResult { Success = false, FailureReason = FailureReason });
    }
}

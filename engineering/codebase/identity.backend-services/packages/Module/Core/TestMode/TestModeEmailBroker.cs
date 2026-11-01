using WoW.Two.Sdk.Beta.Be.Comms.Email;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TestMode;

/// <summary>Takes the email broker's place in test mode: every message lands in the <see cref="TestMessageSink"/> and is accepted, none is sent.</summary>
/// <param name="sink">Keeps the messages.</param>
internal sealed class TestModeEmailBroker(TestMessageSink sink) : IEmailBroker
{
    /// <summary>The provider message id a kept message reports.</summary>
    internal const string MessageId = "test-mode";

    /// <inheritdoc />
    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var text = message.TextBody ?? message.HtmlBody ?? string.Empty;
        foreach (var recipient in message.To)
            sink.Add(OtpChannelNameConstants.Email, recipient.Address, message.Subject, text);

        return Task.FromResult(new EmailSendResult { Success = true, ProviderMessageId = MessageId });
    }
}

using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Comms.Email;
using WoW.Two.Sdk.Beta.Be.Comms.Email.MailKit;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Emails;

/// <summary>Opts the SMTP email delivery into an identity registration.</summary>
public static class EmailDeliveryIdentityBuilderExtensions
{
    /// <summary>
    /// Register the email broker from the host section <c>Identity:Email</c> (<see cref="EmailDeliverySettings"/>): an SMTP
    /// relay through MailKit once <c>Smtp:Host</c> is set, else a broker that skips and logs every send, so an instance
    /// without a relay still boots. A broker the host registered earlier wins.
    /// </summary>
    /// <param name="builder">The identity builder.</param>
    /// <param name="configuration">The configuration the section is read from, when the broker is first used.</param>
    public static IdentityBuilder AddSmtpEmail(this IdentityBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        builder.Services.AddValidatedSettings<EmailDeliverySettings>(
            configuration,
            EmailDeliverySettings.SectionName,
            settings => settings
                .Validate(
                    value => string.IsNullOrWhiteSpace(value.Smtp.Host) || IsAddress(value.From),
                    "Identity:Email:From must be an email address when Identity:Email:Smtp:Host is set.")
                .Validate(value => value.Smtp.Port is > 0 and <= 65_535, "Identity:Email:Smtp:Port must be between 1 and 65535.")
                .Validate(value => Enum.IsDefined(value.Smtp.SecureSocket), "Identity:Email:Smtp:SecureSocket must be a known TLS mode."));
        builder.Services.TryAddSingleton<IEmailBroker>(provider =>
        {
            var settings = provider.GetRequiredService<EmailDeliverySettings>();
            return string.IsNullOrWhiteSpace(settings.Smtp.Host)
                ? new UnconfiguredEmailBroker(provider.GetRequiredService<ILogger<UnconfiguredEmailBroker>>())
                : new MailKitEmailBroker(
                    settings.Smtp,
                    new EmailOptions { DefaultFrom = new EmailAddress { Address = settings.From!, DisplayName = settings.FromName } });
        });
        return builder;
    }

    private static bool IsAddress(string? value) => !string.IsNullOrWhiteSpace(value) && MailAddress.TryCreate(value, out _);
}

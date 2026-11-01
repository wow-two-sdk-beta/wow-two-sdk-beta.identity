using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Comms.Email;
using WoW.Two.Sdk.Beta.Be.Comms.Email.MailKit;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Emails;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

/// <summary>The SMTP relay binds from <c>Identity:Email</c>; without one the instance still starts and every send is skipped.</summary>
public sealed class EmailDeliveryTests
{
    [Fact]
    public async Task Broker_ShouldSkipEverySend_WhenNoSmtpHostIsSet()
    {
        using var provider = Build([]);

        var broker = provider.GetRequiredService<IEmailBroker>();
        var result = await broker.SendAsync(EmailMessage.Create("to@example.test", "Subject", "Body"));

        broker.Should().NotBeOfType<MailKitEmailBroker>();
        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be("smtp_not_configured");
    }

    [Fact]
    public void Broker_ShouldBeTheMailKitSender_WhenTheHostIsSet()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Identity:Email:From"] = "no-reply@id.example",
            ["Identity:Email:FromName"] = "Identity",
            ["Identity:Email:Smtp:Host"] = "mailpit",
            ["Identity:Email:Smtp:Port"] = "1025",
            ["Identity:Email:Smtp:SecureSocket"] = "None",
        });

        provider.GetRequiredService<IEmailBroker>().Should().BeOfType<MailKitEmailBroker>();
        var settings = provider.GetRequiredService<EmailDeliverySettings>();
        settings.Smtp.Host.Should().Be("mailpit");
        settings.Smtp.Port.Should().Be(1025);
        settings.Smtp.SecureSocket.Should().Be(MailKit.Security.SecureSocketOptions.None);
        settings.FromName.Should().Be("Identity");
    }

    [Theory]
    [InlineData("Identity:Email:Smtp:Port", "70000", "Port")]
    [InlineData("Identity:Email:Smtp:SecureSocket", "99", "SecureSocket")]
    public void Settings_ShouldRefuseTheStart_WhenAValueIsInvalidOnItsOwn(string key, string value, string expected)
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Identity:Email:From"] = "no-reply@id.example",
            ["Identity:Email:Smtp:Host"] = "mailpit",
            [key] = value,
        });

        var act = () => provider.GetRequiredService<EmailDeliverySettings>();

        act.Should().Throw<OptionsValidationException>().WithMessage($"*{expected}*");
    }

    [Fact]
    public void Settings_ShouldRefuseTheStart_WhenAHostIsSetWithoutASender()
    {
        using var provider = Build(new Dictionary<string, string?> { ["Identity:Email:Smtp:Host"] = "mailpit" });

        var act = () => provider.GetRequiredService<IEmailBroker>();

        act.Should().Throw<OptionsValidationException>().WithMessage("*Identity:Email:From*");
    }

    [Fact]
    public void Broker_ShouldYieldToOneTheHostRegisteredFirst()
    {
        var own = new OwnBroker();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IEmailBroker>(own);
        services.AddUserAccounts().AddSmtpEmail(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IEmailBroker>().Should().BeSameAs(own);
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddUserAccounts().AddSmtpEmail(configuration);
        return services.BuildServiceProvider();
    }

    private sealed class OwnBroker : IEmailBroker
    {
        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
            => Task.FromResult(new EmailSendResult { Success = true });
    }
}

using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using WoW.Two.Sdk.Beta.Be.Comms.Email;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Emails;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.TestMode;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.BotModes;

/// <summary>
/// Test mode below the HTTP API: while on it swaps the bot client and the email broker for ones that keep every message,
/// switches the bot on with placeholders and without polling, and a deployed boot refuses it; while off nothing changes.
/// </summary>
public sealed class TestModeTests
{
    [Fact]
    public void Sink_ShouldKeepMessagesOldestFirst_AndDropTheOldestPastItsCapacity()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));
        var sink = new TestMessageSink(time);

        for (var i = 0; i < TestMessageSink.Capacity + 5; i++)
        {
            sink.Add("telegram", "1", subject: null, $"m{i}");
            time.Advance(TimeSpan.FromSeconds(1));
        }

        var kept = sink.Snapshot();
        kept.Should().HaveCount(TestMessageSink.Capacity);
        kept[0].Text.Should().Be("m5");
        kept[^1].Text.Should().Be($"m{TestMessageSink.Capacity + 4}");
        kept[0].SentAt.Should().Be(new DateTimeOffset(2026, 11, 1, 9, 0, 5, TimeSpan.Zero));
        sink.Clear();
        sink.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task EmailBroker_ShouldKeepOneMessagePerRecipient_AndAcceptIt()
    {
        var sink = new TestMessageSink(TimeProvider.System);
        var message = EmailMessage.Create("ann@example.test", "Confirm your email", "Open https://id.example.test/confirm-email?token=abc") with
        {
            To = [new EmailAddress { Address = "ann@example.test" }, new EmailAddress { Address = "bob@example.test" }],
        };

        var result = await new TestModeEmailBroker(sink).SendAsync(message);

        result.Success.Should().BeTrue();
        sink.Snapshot().Select(m => (m.Channel, m.To, m.Subject)).Should().Equal(
            ("email", "ann@example.test", "Confirm your email"),
            ("email", "bob@example.test", "Confirm your email"));
        sink.Snapshot()[0].Text.Should().Contain("token=abc");
    }

    [Fact]
    public async Task BotClient_ShouldKeepEveryMessageByChat_AndRefuseAnythingElse()
    {
        var sink = new TestMessageSink(TimeProvider.System);
        ITelegramBotClient bot = new TestModeTelegramBotClient(sink, TimeProvider.System);

        var sent = await bot.SendMessage(4242, "Your code: 123456");

        sent.Text.Should().Be("Your code: 123456");
        sink.Snapshot().Should().ContainSingle().Which.Should().BeEquivalentTo(new { Channel = "telegram", To = "4242", Text = "Your code: 123456" }, o => o.ExcludingMissingMembers());
        await FluentActions.Awaiting(() => bot.SendRequest(new GetUpdatesRequest())).Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public void AddTestMode_ShouldSwapTheClientAndTheBroker_AndSwitchTheBotOn_WhileOn()
    {
        using var provider = Build(testMode: true);

        provider.GetRequiredService<ITelegramBotClient>().Should().BeOfType<TestModeTelegramBotClient>();
        provider.GetRequiredService<IEmailBroker>().Should().BeOfType<TestModeEmailBroker>();
        var bot = provider.GetRequiredService<IOptionsMonitor<TelegramBotOptions>>().CurrentValue;
        bot.IsEnabled.Should().BeTrue("phone sign-in runs on the fake bot");
        bot.UpdateMode.Should().Be(TelegramUpdateMode.Webhook, "a fake bot has nothing to poll");
        bot.BotToken.Should().Be(TestModeIdentityBuilderExtensions.PlaceholderBotToken);
        bot.BotUserName.Should().Be(TestModeIdentityBuilderExtensions.PlaceholderBotUserName);
        bot.WebhookSecret.Should().MatchRegex("^[0-9a-f]{48}$", "a secret nobody knows keeps the real webhook shut");
        provider.GetRequiredService<TestMessageSink>().Should().NotBeNull();
    }

    [Fact]
    public void AddTestMode_ShouldKeepTheHostsOwnBotSettings_ButNeverPollOrCallItsClient()
    {
        using var provider = Build(testMode: true, bot =>
        {
            bot.BotToken = "777:real-looking";
            bot.BotUserName = "Real_Bot";
            bot.WebhookSecret = "mine";
            bot.UpdateMode = TelegramUpdateMode.Polling;
        });

        var bot = provider.GetRequiredService<IOptionsMonitor<TelegramBotOptions>>().CurrentValue;
        (bot.BotToken, bot.BotUserName, bot.WebhookSecret).Should().Be(("777:real-looking", "Real_Bot", "mine"));
        bot.UpdateMode.Should().Be(TelegramUpdateMode.Webhook);
        provider.GetRequiredService<ITelegramBotClient>().Should().BeOfType<TestModeTelegramBotClient>();
    }

    [Fact]
    public void AddTestMode_ShouldChangeNothing_WhileOff()
    {
        using var provider = Build(testMode: false, bot => bot.BotToken = "1:abc");

        provider.GetRequiredService<ITelegramBotClient>().Should().BeOfType<TelegramBotClient>().Which.BotId.Should().Be(1);
        provider.GetRequiredService<IEmailBroker>().GetType().Name.Should().Be("UnconfiguredEmailBroker");
        provider.GetRequiredService<IOptionsMonitor<TelegramBotOptions>>().CurrentValue.IsEnabled.Should().BeFalse();
        provider.GetRequiredService<TestModeOptions>().IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void AddTestMode_ShouldKeepAnEarlierRegistrationAsItWas_WhileOff()
    {
        var own = new OwnBroker();
        using var provider = Build(testMode: false, broker: own);

        provider.GetRequiredService<IEmailBroker>().Should().BeSameAs(own);
    }

    [Fact]
    public async Task Simulator_ShouldAnswerFalse_WhileOff()
    {
        using var provider = Build(testMode: false);
        using var scope = provider.CreateScope();

        var done = await scope.ServiceProvider.GetRequiredService<IPhoneSignInLinkSimulator>()
            .TryCompleteAsync("+998901234567", "https://t.me/x?start=abc", System.Globalization.CultureInfo.InvariantCulture);

        done.Should().BeFalse();
    }

    [Theory]
    [InlineData("Production", null, false)]
    [InlineData("Development", "dev", false)]
    [InlineData("Development", null, true)]
    public void Validator_ShouldRefuseTestMode_OnADeployedBoot(string environment, string? deployEnvironment, bool isAllowed)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(deployEnvironment is null ? [] : new Dictionary<string, string?> { ["DEPLOY_ENVIRONMENT"] = deployEnvironment })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new Environment(environment));
        services.AddSingleton<IConfiguration>(configuration);
        using var provider = services.BuildServiceProvider();

        var result = new TestModeOptionsValidator(provider).Validate(null, new TestModeOptions { IsEnabled = true });

        result.Succeeded.Should().Be(isAllowed);
        if (!isAllowed)
            result.Failures.Should().ContainSingle().Which.Should().Contain("Identity:TestMode:IsEnabled").And.Contain("local run");
        new TestModeOptionsValidator(provider).Validate(null, new TestModeOptions { IsEnabled = false }).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validator_ShouldRefuseTestMode_WhenNoEnvironmentCanBeJudged()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        new TestModeOptionsValidator(provider).Validate(null, new TestModeOptions { IsEnabled = true }).Failed.Should().BeTrue();
    }

    [Fact]
    public void Boot_ShouldFail_WhenTestModeIsOnOnADeployedBoot()
    {
        using var provider = Build(testMode: true, environment: "Production");

        var act = () => provider.GetRequiredService<TestModeOptions>();

        act.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain("local run");
    }

    private static ServiceProvider Build(bool testMode, Action<TelegramBotOptions>? bot = null, IEmailBroker? broker = null, string environment = "Development")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Identity:TestMode:IsEnabled"] = testMode ? "true" : "false" })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new Environment(environment));
        services.AddSingleton<ITelegramLinkRepository>(new InMemoryTelegramLinkRepository());
        if (broker is not null)
            services.AddSingleton(broker);

        var identity = new IdentityBuilder(services);
        identity.AddSmtpEmail(configuration);
        identity.AddTelegramLinks(options => bot?.Invoke(options));
        identity.AddTestMode();
        return services.BuildServiceProvider();
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class OwnBroker : IEmailBroker
    {
        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
            => Task.FromResult(new EmailSendResult { Success = true });
    }
}

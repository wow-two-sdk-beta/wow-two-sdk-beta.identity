using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.BotModes;

/// <summary>The bot's boot check for the update modes: polling only on a local run, the webhook secret only where the webhook runs, and a usable webhook address.</summary>
public sealed class TelegramBotOptionsValidatorTests
{
    [Fact]
    public void Polling_ShouldBeRefused_OnADeployedBoot_AndAllowedOnALocalRun()
    {
        var deployed = Validate("Production", bot => bot.UpdateMode = TelegramUpdateMode.Polling);
        var local = Validate("Development", bot => bot.UpdateMode = TelegramUpdateMode.Polling);

        deployed.Failed.Should().BeTrue();
        deployed.Failures.Should().ContainSingle().Which.Should().Contain("UpdateMode").And.Contain("Polling").And.Contain("local run");
        local.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Polling_ShouldBeRefused_WhenTheDeployEnvironmentMakesDevelopmentADeployedHost()
    {
        var result = Validate("Development", bot => bot.UpdateMode = TelegramUpdateMode.Polling, deployEnvironment: "dev");

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(failure => failure.Contains("Polling"));
    }

    [Fact]
    public void Boot_ShouldFail_WhenPollingIsOnADeployedBoot()
    {
        using var host = TelegramTestHost.Create(bot => bot.UpdateMode = TelegramUpdateMode.Polling, environment: "Production");

        var act = () => host.Provider.GetRequiredService<IOptionsMonitor<TelegramBotOptions>>().CurrentValue;

        act.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain("Polling").And.Contain("local run");
    }

    [Fact]
    public void WebhookSecret_ShouldBeRequiredInWebhookMode_ButNotInPollingMode()
    {
        var webhook = Validate("Development", bot => bot.WebhookSecret = null);
        var polling = Validate("Development", bot =>
        {
            bot.WebhookSecret = null;
            bot.UpdateMode = TelegramUpdateMode.Polling;
        });

        webhook.Failures.Should().ContainSingle().Which.Should().Contain("WebhookSecret");
        polling.Succeeded.Should().BeTrue("polling receives no secret header");
    }

    [Fact]
    public void WebhookSecret_ShouldStillBeChecked_WhenGivenInPollingMode()
    {
        var result = Validate("Development", bot =>
        {
            bot.UpdateMode = TelegramUpdateMode.Polling;
            bot.WebhookSecret = "not allowed!";
        });

        result.Failures.Should().ContainSingle().Which.Should().Contain("WebhookSecret");
    }

    [Theory]
    [InlineData("http://id.example.test/api/telegram/webhook")]
    [InlineData("/api/telegram/webhook")]
    public void WebhookUrl_ShouldBeRefused_UnlessItIsAnAbsoluteHttpsAddress(string url)
    {
        var result = Validate("Development", bot => bot.WebhookUrl = url);

        result.Failures.Should().ContainSingle().Which.Should().Contain("WebhookUrl");
    }

    [Fact]
    public void Validator_ShouldAcceptAnHttpsWebhookUrl_AndRefuseAnUnknownMode()
    {
        Validate("Production", bot => bot.WebhookUrl = "https://id.example.test/api/telegram/webhook").Succeeded.Should().BeTrue();
        Validate("Development", bot => bot.UpdateMode = (TelegramUpdateMode)7).Failures.Should().Contain(failure => failure.Contains("UpdateMode"));
    }

    [Fact]
    public void Validator_ShouldAskNothing_WhileTheBotIsOff()
    {
        var result = Validate("Production", bot =>
        {
            bot.IsEnabled = false;
            bot.UpdateMode = TelegramUpdateMode.Polling;
            bot.BotToken = null;
        });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validator_ShouldStillAskForTheCodeKey_OnADeployedBoot()
    {
        var result = Validate("Production", _ => { }, hasHashKey: false);

        result.Failures.Should().ContainSingle().Which.Should().Contain("HashKey");
    }

    private static ValidateOptionsResult Validate(string environment, Action<TelegramBotOptions> shape, string? deployEnvironment = null, bool hasHashKey = true)
    {
        using var host = TelegramTestHost.Create(
            environment: environment,
            configuration: deployEnvironment is null ? null : new Dictionary<string, string?> { ["DEPLOY_ENVIRONMENT"] = deployEnvironment },
            hasHashKey: hasHashKey);
        var options = new TelegramBotOptions { IsEnabled = true, BotToken = TelegramTestHost.Token, BotUserName = "Ocharo_Bot", WebhookSecret = "hook-secret" };
        shape(options);
        return new TelegramBotOptionsValidator(host.Provider).Validate(null, options);
    }
}

using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.BotModes;

/// <summary>
/// A deployed instance's webhook registration: <c>setWebhook</c> runs only when the address or the allowed updates differ (or
/// Telegram's last delivery was refused with 401), never on a local run, in polling mode or while the bot is off, and a
/// failure is logged without stopping the start.
/// </summary>
public sealed class TelegramWebhookRegistrationServiceTests
{
    private const string Url = "https://id.example.test/api/telegram/webhook";

    [Fact]
    public async Task Start_ShouldRegisterTheWebhook_WhenNoneIsSet()
    {
        using var host = Deployed();

        await RunAsync(host);

        var set = host.Bot.SetWebhooks.Should().ContainSingle().Which;
        set.Url.Should().Be(Url);
        set.SecretToken.Should().Be("hook-secret");
        set.AllowedUpdates.Should().Equal(new[] { UpdateType.Message, UpdateType.CallbackQuery }, "button presses arrive as callback queries");
        host.Bot.Methods.Should().Equal("getWebhookInfo", "setWebhook");
    }

    [Fact]
    public async Task Start_ShouldCallNothingMore_WhenTheAddressAndUpdatesAlreadyMatch()
    {
        using var host = Deployed();
        host.Bot.Webhook = new WebhookInfo { Url = Url, AllowedUpdates = [UpdateType.CallbackQuery, UpdateType.Message] };

        await RunAsync(host);

        host.Bot.SetWebhooks.Should().BeEmpty("the order Telegram reports them in does not matter");
        host.Bot.Methods.Should().Equal("getWebhookInfo");
    }

    [Theory]
    [InlineData("https://old.example.test/api/telegram/webhook", "Message,CallbackQuery", null)]
    [InlineData(Url, "Message", null)]
    [InlineData(Url, "Message,CallbackQuery,InlineQuery", null)]
    [InlineData(Url, "", null)]
    [InlineData(Url, "Message,CallbackQuery", "Wrong response from the webhook: 401 Unauthorized")]
    public async Task Start_ShouldRegister_WhenTheAddressOrUpdatesDifferOrTheSecretWasRefused(string registeredUrl, string registeredUpdates, string? lastError)
    {
        using var host = Deployed();
        host.Bot.Webhook = new WebhookInfo
        {
            Url = registeredUrl,
            AllowedUpdates = [.. registeredUpdates.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<UpdateType>)],
            LastErrorMessage = lastError,
        };

        await RunAsync(host);

        host.Bot.SetWebhooks.Should().ContainSingle().Which.Url.Should().Be(Url);
    }

    [Fact]
    public async Task Start_ShouldLeaveTheWebhookAlone_WhenTheLastDeliveryFailedForAnotherReason()
    {
        using var host = Deployed();
        host.Bot.Webhook = new WebhookInfo { Url = Url, AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery], LastErrorMessage = "Connection timed out" };

        await RunAsync(host);

        host.Bot.SetWebhooks.Should().BeEmpty("only a refused secret is worth a re-registration");
    }

    [Fact]
    public async Task Start_ShouldNotTouchTelegram_OnALocalRun_InPollingMode_WhileTheBotIsOff_OrWithNoAddress()
    {
        using var local = Deployed(environment: "Development");
        using var polling = Deployed(bot => bot.UpdateMode = TelegramUpdateMode.Polling, environment: "Development");
        using var off = Deployed(bot => bot.IsEnabled = false);
        using var unaddressed = Deployed(bot => bot.WebhookUrl = null);

        foreach (var host in new[] { local, polling, off, unaddressed })
            await RunAsync(host);

        foreach (var host in new[] { local, polling, off })
            host.Bot.Methods.Should().BeEmpty();
        unaddressed.Bot.Methods.Should().BeEmpty("with no address there is nothing to compare or register");
        unaddressed.Log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("No public address"));
    }

    [Fact]
    public async Task Start_ShouldLogAndNotFail_WhenTelegramRefuses()
    {
        using var reading = Deployed();
        reading.Bot.WebhookInfoFailure = new RequestException("no route to Telegram");
        using var setting = Deployed();
        setting.Bot.SetWebhookFailure = new ApiRequestException("Bad Request: bad webhook: HTTPS url must be provided for webhook", 400);

        await RunAsync(reading);
        await RunAsync(setting);

        reading.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Which.Message.Should().Contain("no route to Telegram");
        setting.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Which.Message.Should().Contain("HTTPS url");
    }

    private static TelegramTestHost Deployed(Action<TelegramBotOptions>? bot = null, string environment = "Production")
        => TelegramTestHost.Create(
            options =>
            {
                options.WebhookUrl = Url;
                bot?.Invoke(options);
            },
            environment);

    /// <summary>Starts the registration as the host does, which returns at once, then waits for its work to end.</summary>
    private static async Task RunAsync(TelegramTestHost host)
    {
        var registration = host.Service<TelegramWebhookRegistrationService>();
        await registration.StartAsync(CancellationToken.None);
        await registration.ExecuteTask!;
        await registration.StopAsync(CancellationToken.None);
    }
}

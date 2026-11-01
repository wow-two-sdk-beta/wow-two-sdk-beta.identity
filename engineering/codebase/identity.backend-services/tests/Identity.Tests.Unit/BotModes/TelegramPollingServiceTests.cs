using AwesomeAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.BotModes;

/// <summary>
/// The local run's polling: updates from <c>getUpdates</c> reach the same handler the webhook uses and are confirmed by
/// offset, a webhook-held bot logs one clear error and backs off without ever touching the webhook, and other failures and
/// poisoned updates never stop the loop.
/// </summary>
public sealed class TelegramPollingServiceTests
{
    private const string WebhookConflict = "Conflict: can't use getUpdates method while webhook is active; use deleteWebhook to delete the webhook first";

    [Fact]
    public async Task Polling_ShouldFeedEachUpdateToTheHandler_AndConfirmThemByOffset()
    {
        using var host = TelegramTestHost.Create(bot => bot.UpdateMode = TelegramUpdateMode.Polling, environment: "Development");
        host.Bot.Enqueue(BareStart(updateId: 10, chatId: 501));
        host.Bot.Enqueue(BareStart(updateId: 11, chatId: 502));
        var polling = host.Service<TelegramPollingService>();

        await polling.StartAsync(CancellationToken.None);
        await TelegramTestHost.EventuallyAsync(() => host.Bot.Sent.Count == 2 && host.Bot.PendingCount == 0);
        await polling.StopAsync(CancellationToken.None);

        host.Bot.Sent.Select(m => m.ChatId.Identifier).Should().BeEquivalentTo(new long?[] { 501, 502 }, "the same handler that the webhook uses answered both");
        host.Bot.Polls.First().Offset.Should().BeNull("the first poll takes whatever Telegram holds");
        host.Bot.Polls.Select(p => p.Offset).Should().Contain(12, "the next poll confirms update 11 by asking from 12");
        host.Bot.Polls.Should().OnlyContain(p => p.AllowedUpdates!.SequenceEqual(new[] { UpdateType.Message }));
        host.Bot.Methods.Should().NotContain("deleteWebhook").And.NotContain("setWebhook");
    }

    [Fact]
    public async Task Polling_ShouldLogOneClearError_AndBackOff_WhileAWebhookHoldsTheBot()
    {
        using var host = TelegramTestHost.Create(bot => bot.UpdateMode = TelegramUpdateMode.Polling, environment: "Development");
        for (var refusal = 0; refusal < 3; refusal++)
            host.Bot.PollFailures.Enqueue(new ApiRequestException(WebhookConflict, 409));
        host.Bot.Enqueue(BareStart(updateId: 20, chatId: 601));
        var polling = host.Service<TelegramPollingService>();

        await polling.StartAsync(CancellationToken.None);
        await TelegramTestHost.EventuallyAsync(() => host.Bot.Sent.Count == 1);
        await polling.StopAsync(CancellationToken.None);

        host.Bot.Polls.Count.Should().BeGreaterThanOrEqualTo(4, "three refused polls, then one that goes through once the webhook is gone");
        var errors = host.Log.Entries.Where(e => e.Level == LogLevel.Error).ToList();
        errors.Should().ContainSingle("a webhook-held bot is reported once per episode, not once per retry");
        errors[0].Message.Should().Contain("webhook is set").And.Contain("hosted instance").And.Contain("own bot").And.Contain("TestMode");
        host.Bot.Methods.Should().NotContain("deleteWebhook", "the webhook belongs to the hosted instance");
    }

    [Fact]
    public async Task Polling_ShouldReportAnotherPoller_AsItsOwnError()
    {
        using var host = TelegramTestHost.Create(bot => bot.UpdateMode = TelegramUpdateMode.Polling, environment: "Development");
        host.Bot.PollFailures.Enqueue(new ApiRequestException("Conflict: terminated by other getUpdates request; make sure that only one bot instance is running", 409));
        var polling = host.Service<TelegramPollingService>();

        await polling.StartAsync(CancellationToken.None);
        await TelegramTestHost.EventuallyAsync(() => host.Bot.Polls.Count >= 2);
        await polling.StopAsync(CancellationToken.None);

        host.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Which.Message.Should().Contain("Another instance is polling");
    }

    [Fact]
    public async Task Polling_ShouldKeepGoing_AfterAFailedPollAndAPoisonedUpdate()
    {
        using var host = TelegramTestHost.Create(bot => bot.UpdateMode = TelegramUpdateMode.Polling, environment: "Development");
        host.Bot.PollFailures.Enqueue(new HttpRequestException("network down"));
        host.Links.IsPendingLookupFailing = true;
        host.Bot.Enqueue(StartWith(updateId: 30, chatId: 701, token: "poison"));
        host.Bot.Enqueue(BareStart(updateId: 31, chatId: 702));
        var polling = host.Service<TelegramPollingService>();

        await polling.StartAsync(CancellationToken.None);
        await TelegramTestHost.EventuallyAsync(() => host.Bot.Sent.Count == 1 && host.Bot.PendingCount == 0);
        await polling.StopAsync(CancellationToken.None);

        host.Bot.Sent.Single().ChatId.Identifier.Should().Be(702, "the update behind the poisoned one was still handled");
        host.Log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("network down"));
        host.Log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("update 30"));
    }

    [Fact]
    public async Task Service_ShouldNotPoll_InWebhookMode()
    {
        using var host = TelegramTestHost.Create();
        host.Bot.Enqueue(BareStart(updateId: 40, chatId: 801));
        var polling = host.Service<TelegramPollingService>();

        await polling.StartAsync(CancellationToken.None);
        await polling.ExecuteTask!;
        await polling.StopAsync(CancellationToken.None);

        host.Bot.Polls.Should().BeEmpty();
        host.Bot.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Service_ShouldNeitherPollNorResolveTheBot_WhileTheBotIsOff()
    {
        using var host = TelegramTestHost.Create(
            bot =>
            {
                bot.IsEnabled = false;
                bot.BotToken = null;
                bot.UpdateMode = TelegramUpdateMode.Polling;
            },
            environment: "Development",
            withoutBotClient: true);
        var polling = host.Service<TelegramPollingService>();

        await polling.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await polling.StopAsync(CancellationToken.None);

        polling.ExecuteTask!.Status.Should().Be(TaskStatus.RanToCompletion, "an instance with the bot off needs no token to start");
        host.Bot.Polls.Should().BeEmpty();
    }

    private static Update BareStart(int updateId, long chatId) => StartWith(updateId, chatId, token: null);

    private static Update StartWith(int updateId, long chatId, string? token) => new()
    {
        Id = updateId,
        Message = new Message
        {
            Id = updateId,
            Date = DateTime.UtcNow,
            Chat = new Chat { Id = chatId, Type = ChatType.Private },
            From = new User { Id = chatId, IsBot = false, FirstName = "Lola", LanguageCode = "en" },
            Text = token is null ? "/start" : "/start " + token,
        },
    };
}

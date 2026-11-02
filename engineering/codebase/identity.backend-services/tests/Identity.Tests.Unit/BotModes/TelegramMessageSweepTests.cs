using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Telegram.Bot.Exceptions;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.BotModes;

/// <summary>
/// The bot's message sweep: due messages sent under 48 hours ago are deleted per chat in calls of at most 100 ids, those
/// 48 hours or older become the one-line stub with no buttons, a message Telegram no longer finds counts as done, and a
/// failure is logged and retried without ever stopping the host.
/// </summary>
public sealed class TelegramMessageSweepTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sweep_ShouldDeleteDueMessagesSentUnder48HoursAgo_PerChatInCallsOfAtMost100()
    {
        using var host = Create();
        for (var id = 1; id <= 150; id++)
            host.Ledger.Seed(chatId: 501, messageId: id, sentAt: Now.AddHours(-1), deleteAt: Now.AddMinutes(-1));
        host.Ledger.Seed(chatId: 502, messageId: 999, sentAt: Now.AddHours(-47), deleteAt: Now);
        var waiting = host.Ledger.Seed(chatId: 503, messageId: 8, sentAt: Now.AddMinutes(-1), deleteAt: Now.AddMinutes(4));

        var swept = await host.SweepAsync();

        swept.Should().Be(151);
        host.Bot.Deletions.Select(d => $"{d.ChatId.Identifier}:{d.MessageIds.Count()}").Should().Equal("501:100", "501:50", "502:1");
        host.Bot.Deletions.Where(d => d.ChatId.Identifier == 501).SelectMany(d => d.MessageIds).Should().BeEquivalentTo(Enumerable.Range(1, 150));
        host.Bot.TextEdits.Should().BeEmpty("nothing due is 48 hours old");
        host.Ledger.Messages.Should().ContainSingle().Which.Should().BeSameAs(waiting, "a message not yet due stays");
    }

    [Fact]
    public async Task Sweep_ShouldEditMessages48HoursOrOlder_ToTheStubInTheirCulture_WithNoButtons()
    {
        using var host = Create();
        host.Ledger.Seed(chatId: 601, messageId: 1, sentAt: Now.AddHours(-48), deleteAt: Now.AddHours(-47), culture: "ru");
        host.Ledger.Seed(chatId: 602, messageId: 2, sentAt: Now.AddDays(-3), deleteAt: Now.AddDays(-2));
        host.Ledger.Seed(chatId: 603, messageId: 3, sentAt: Now.AddHours(-1), deleteAt: Now);

        (await host.SweepAsync()).Should().Be(3);

        host.Bot.TextEdits.Select(e => (e.ChatId.Identifier, e.MessageId, e.Text)).Should().BeEquivalentTo(new (long?, int, string)[]
        {
            (601L, 1, "Срок действия этого сообщения истёк."),
            (602L, 2, "This message has expired."),
        });
        host.Bot.TextEdits.Should().OnlyContain(e => e.ReplyMarkup == null, "the stub carries no buttons");
        host.Bot.Deletions.Should().ContainSingle().Which.ChatId.Identifier.Should().Be(603, "only the message under 48 hours is deleted");
        host.Ledger.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Sweep_ShouldWordTheStubAsConfigured()
    {
        using var host = Create(bot => bot.Messages["en"] = new Dictionary<string, string> { [TelegramBotMessageKeyConstants.MessageExpired] = "Gone." });
        host.Ledger.Seed(chatId: 601, messageId: 1, sentAt: Now.AddDays(-5), deleteAt: Now.AddDays(-4), culture: "en");

        await host.SweepAsync();

        host.Bot.TextEdits.Should().ContainSingle().Which.Text.Should().Be("Gone.");
    }

    [Fact]
    public async Task Sweep_ShouldCountAMessageTelegramNoLongerFinds_AsDone()
    {
        using var host = Create();
        host.Bot.ChatFailures[701] = new ApiRequestException("Bad Request: message to delete not found", 400);
        host.Bot.EditFailures[(702, 2)] = new ApiRequestException("Bad Request: message to edit not found", 400);
        host.Bot.EditFailures[(703, 3)] = new ApiRequestException("Bad Request: message is not modified: specified new message content and reply markup are exactly the same", 400);
        host.Ledger.Seed(chatId: 701, messageId: 1, sentAt: Now.AddHours(-2), deleteAt: Now.AddHours(-1));
        host.Ledger.Seed(chatId: 702, messageId: 2, sentAt: Now.AddDays(-3), deleteAt: Now.AddDays(-2));
        host.Ledger.Seed(chatId: 703, messageId: 3, sentAt: Now.AddDays(-3), deleteAt: Now.AddDays(-2));

        (await host.SweepAsync()).Should().Be(3);

        host.Ledger.Messages.Should().BeEmpty("a message that is gone, or already the stub, needs nothing more");
        host.Log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Sweep_ShouldFallBackToTheStub_WhenTelegramRefusesTheDeletion()
    {
        using var host = Create();
        host.Bot.ChatFailures[801] = new ApiRequestException("Bad Request: message can't be deleted for everyone", 400);
        host.Ledger.Seed(chatId: 801, messageId: 1, sentAt: Now.AddHours(-47).AddMinutes(-59), deleteAt: Now.AddHours(-1));

        (await host.SweepAsync()).Should().Be(1);

        host.Bot.Deletions.Should().ContainSingle();
        host.Bot.TextEdits.Should().ContainSingle().Which.Text.Should().Be("This message has expired.");
        host.Ledger.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Sweep_ShouldLeaveAChatThatRefusesTheBot_AsItIs()
    {
        using var host = Create();
        host.Bot.ChatFailures[901] = new ApiRequestException("Forbidden: bot was blocked by the user", 403);
        host.Ledger.Seed(chatId: 901, messageId: 1, sentAt: Now.AddHours(-2), deleteAt: Now.AddHours(-1));

        (await host.SweepAsync()).Should().Be(1);

        host.Ledger.Messages.Should().BeEmpty("nothing in a chat that blocked the bot can change");
        host.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Which.Message.Should().Contain("blocked");
    }

    [Fact]
    public async Task Sweep_ShouldKeepTheRows_AndLog_WhenTelegramFailsOtherwise_ThenCleanThemUpNextTime()
    {
        using var host = Create();
        host.Bot.ChatFailures[1001] = new RequestException("no route to Telegram");
        host.Bot.EditFailures[(1002, 2)] = new ApiRequestException("Internal Server Error", 500);
        host.Ledger.Seed(chatId: 1001, messageId: 1, sentAt: Now.AddHours(-2), deleteAt: Now.AddHours(-1));
        host.Ledger.Seed(chatId: 1002, messageId: 2, sentAt: Now.AddDays(-3), deleteAt: Now.AddDays(-2));

        (await host.SweepAsync()).Should().Be(0);

        host.Ledger.Messages.Should().HaveCount(2, "a passing failure keeps the rows due");
        host.Log.Entries.Where(e => e.Level == LogLevel.Warning).Should().HaveCount(2);

        host.Bot.ChatFailures.Clear();
        host.Bot.EditFailures.Clear();
        (await host.SweepAsync()).Should().Be(2);
        host.Ledger.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Sweep_ShouldTouchOnlyTheCurrentBotsMessages()
    {
        using var host = Create();
        host.Ledger.Seed(chatId: 501, messageId: 1, sentAt: Now.AddHours(-2), deleteAt: Now.AddHours(-1), botUserName: "old_bot");

        (await host.SweepAsync()).Should().Be(0);

        host.Bot.Methods.Should().BeEmpty("another bot's messages are beyond this token");
        host.Ledger.Messages.Should().ContainSingle();
    }

    [Fact]
    public async Task SweepService_ShouldRunEachInterval_AndKeepGoingAfterAFailedSweep()
    {
        using var host = Create(services: services => services.AddSingleton(new TelegramMessageSweepSchedule { Interval = TimeSpan.FromMilliseconds(10) }));
        host.Ledger.FindDueFailures.Enqueue(new InvalidOperationException("database down"));
        host.Ledger.Seed(chatId: 501, messageId: 1, sentAt: Now.AddMinutes(-6), deleteAt: Now.AddMinutes(-1));
        var service = host.Service<TelegramMessageSweepService>();

        await service.StartAsync(CancellationToken.None);
        await TelegramTestHost.EventuallyAsync(() => host.Ledger.Messages.Count == 0);
        await service.StopAsync(CancellationToken.None);

        host.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Which.Message.Should().Contain("database down");
        host.Bot.Deletions.Should().ContainSingle("the sweep after the failed one cleaned up");
        service.ExecuteTask!.IsFaulted.Should().BeFalse("a failed sweep never stops the host");
    }

    [Fact]
    public async Task SweepService_ShouldLookAtNothing_WhileTheBotIsOff()
    {
        using var host = Create(
            bot => bot.IsEnabled = false,
            services: services => services.AddSingleton(new TelegramMessageSweepSchedule { Interval = TimeSpan.FromMilliseconds(5) }));
        host.Ledger.Seed(chatId: 501, messageId: 1, sentAt: Now.AddMinutes(-6), deleteAt: Now.AddMinutes(-1));
        var service = host.Service<TelegramMessageSweepService>();

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(60));
        await service.StopAsync(CancellationToken.None);

        host.Ledger.FindDueCount.Should().Be(0);
        host.Bot.Methods.Should().BeEmpty();
    }

    private static TelegramTestHost Create(Action<TelegramBotOptions>? bot = null, Action<IServiceCollection>? services = null)
        => TelegramTestHost.Create(bot, time: new FakeTimeProvider(Now), services: services);
}

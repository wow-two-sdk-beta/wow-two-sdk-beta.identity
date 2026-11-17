using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Identity.Notices;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Notices;

/// <summary>
/// The pace of app notices: each app inside its own rate limit, one notice a second per chat, and the bot inside
/// <see cref="AppNoticePace.BotMessagesPerSecond"/>; a turn further off than the longest wait is refused with the time to it.
/// </summary>
public sealed class AppNoticePaceTests
{
    [Fact]
    public void TryAdmit_ShouldCountEachAppApart_AndAnswerARetry()
    {
        using var pace = new AppNoticePace(Options.Create(new AppNoticeOptions { PermitLimit = 2, Window = TimeSpan.FromMinutes(1) }));

        pace.TryAdmit("events", out _).Should().BeTrue();
        pace.TryAdmit("events", out _).Should().BeTrue();
        pace.TryAdmit("events", out var retryAfter).Should().BeFalse("past the app's limit");
        retryAfter.Should().BePositive();
        pace.TryAdmit("desk", out _).Should().BeTrue("another app keeps its own limit");
    }

    [Fact]
    public async Task WaitTurn_ShouldKeepOneSecondPerChat_AndRefuseATurnPastTheLongestWait()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));
        using var pace = new AppNoticePace(Options.Create(new AppNoticeOptions()), time);

        (await pace.WaitTurnAsync(1, TimeSpan.FromSeconds(5))).Should().BeNull("the chat's first turn is now");
        (await pace.WaitTurnAsync(2, TimeSpan.Zero)).Should().Be(TimeSpan.FromMilliseconds(40), "another chat waits only for the bot's pace");
        var refused = await pace.WaitTurnAsync(1, TimeSpan.FromMilliseconds(500));
        refused.Should().BeCloseTo(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1), "the chat's next turn is a second after its first");

        var waiting = pace.WaitTurnAsync(1, TimeSpan.FromSeconds(5));
        waiting.IsCompleted.Should().BeFalse("the turn is a second off");
        time.Advance(TimeSpan.FromSeconds(1));
        (await waiting).Should().BeNull();
    }

    [Fact]
    public async Task WaitTurn_ShouldSpreadTheBotsNoticesAcrossTheSecond()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));
        using var pace = new AppNoticePace(Options.Create(new AppNoticeOptions()), time);

        for (var chat = 0; chat < AppNoticePace.BotMessagesPerSecond; chat++)
        {
            var turn = pace.WaitTurnAsync(chat, TimeSpan.FromSeconds(5));
            time.Advance(TimeSpan.FromSeconds(1.0 / AppNoticePace.BotMessagesPerSecond));
            (await turn).Should().BeNull();
        }

        (await pace.WaitTurnAsync(999, TimeSpan.Zero)).Should().BeNull("the bot has spent a second's notices over a second");
    }
}

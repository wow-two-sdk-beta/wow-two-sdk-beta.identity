using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.BotModes;

/// <summary>
/// Presses of a notice's inline buttons (<c>callback_query</c>): routed to the handler keyed by the button's action, only
/// for the allowed Telegram user and only once, the notice losing its buttons, and every query answered, whether it was
/// handled, refused or failed. Polling hands callback queries to the same router.
/// </summary>
public sealed class TelegramCallbackRoutingTests
{
    private const string Revoke = "test.revoke";
    private const long Owner = 501;
    private const long Stranger = 777;
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Press_ShouldRunTheActionsHandler_ReplaceTheText_DropTheButtons_AndAnswer()
    {
        using var host = Create();
        var data = await NoticeAsync(host, TelegramNoticeSenderTests.Button(Revoke, "contact-42"), TelegramNoticeSenderTests.Button("test.keep", null));
        var notice = host.Ledger.Messages.Single();

        await PressAsync(host, data[0], from: Owner);

        host.Provider.GetRequiredService<PressLog>().Calls.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Action = Revoke,
            Payload = "contact-42",
            Kind = "contact-sign-in",
            UserId = notice.UserId,
            ChatId = Owner,
            MessageId = notice.MessageId,
            TelegramUserId = Owner,
        });
        var edit = host.Bot.TextEdits.Should().ContainSingle().Which;
        edit.ChatId.Identifier.Should().Be(Owner);
        edit.MessageId.Should().Be(notice.MessageId);
        edit.Text.Should().Be("Trust revoked");
        edit.ReplyMarkup.Should().BeNull("the buttons drop");
        host.Bot.Answers.Should().ContainSingle().Which.Should().BeEquivalentTo(new { CallbackQueryId = "q1", Text = "Done" }, options => options.ExcludingMissingMembers());
        host.Ledger.Callbacks.Should().OnlyContain(c => c.UsedAt == Now, "pressing one button spends every button of the notice");
    }

    [Fact]
    public async Task Press_ShouldBeRefused_FromAnotherTelegramUser_AndLeaveTheButtonToItsOwn()
    {
        using var host = Create();
        var data = await NoticeAsync(host, TelegramNoticeSenderTests.Button(Revoke, "contact-42"));

        await PressAsync(host, data[0], from: Stranger, queryId: "q1");

        host.Provider.GetRequiredService<PressLog>().Calls.Should().BeEmpty();
        host.Bot.TextEdits.Should().BeEmpty();
        host.Bot.MarkupEdits.Should().BeEmpty();
        host.Bot.Answers.Should().ContainSingle().Which.Text.Should().Be("This button is not for you.");
        host.Ledger.Callbacks.Should().OnlyContain(c => c.UsedAt == null, "a stranger's press spends nothing");

        await PressAsync(host, data[0], from: Owner, queryId: "q2");

        host.Provider.GetRequiredService<PressLog>().Calls.Should().ContainSingle();
        host.Bot.Answers.Select(a => a.CallbackQueryId).Should().Equal("q1", "q2");
    }

    [Fact]
    public async Task Press_ShouldCountOnce_ForTheButtonAndItsSiblings()
    {
        using var host = Create();
        var data = await NoticeAsync(host, TelegramNoticeSenderTests.Button(Revoke, "contact-42"), TelegramNoticeSenderTests.Button(Revoke, "contact-43"));

        await PressAsync(host, data[0], from: Owner, queryId: "q1");
        await PressAsync(host, data[0], from: Owner, queryId: "q2");
        await PressAsync(host, data[1], from: Owner, queryId: "q3");

        host.Provider.GetRequiredService<PressLog>().Calls.Should().ContainSingle();
        host.Bot.Answers.Select(a => (a.CallbackQueryId, a.Text)).Should().Equal(
            ("q1", "Done"),
            ("q2", "This button no longer works."),
            ("q3", "This button no longer works."));
    }

    [Fact]
    public async Task Press_ShouldOnlyDropTheButtons_WhenTheHandlerGivesNoText()
    {
        using var host = Create();
        host.Provider.GetRequiredService<PressLog>().Result = TelegramCallbackResult.Done;
        var data = await NoticeAsync(host, TelegramNoticeSenderTests.Button(Revoke, "contact-42"));

        await PressAsync(host, data[0], from: Owner);

        host.Bot.TextEdits.Should().BeEmpty();
        var markup = host.Bot.MarkupEdits.Should().ContainSingle().Which;
        markup.ReplyMarkup.Should().BeNull();
        host.Bot.Answers.Should().ContainSingle().Which.Text.Should().BeNull();
    }

    [Fact]
    public async Task Press_ShouldStillBeAnswered_ForAnUnknownOrOversizedToken()
    {
        using var host = Create();

        await PressAsync(host, "no-such-token", from: Owner, queryId: "q1");
        await PressAsync(host, new string('x', 65), from: Owner, queryId: "q2");

        host.Bot.Answers.Select(a => (a.CallbackQueryId, a.Text)).Should().Equal(
            ("q1", "This button no longer works."),
            ("q2", "This button no longer works."));
        host.Bot.Methods.Should().OnlyContain(method => method == "answerCallbackQuery");
    }

    [Fact]
    public async Task Press_ShouldBeAnsweredAndDropTheButtons_WhenNoHandlerServesTheAction()
    {
        using var host = Create();
        var data = await NoticeAsync(host, TelegramNoticeSenderTests.Button("test.unserved", null));

        await PressAsync(host, data[0], from: Owner);

        host.Bot.MarkupEdits.Should().ContainSingle();
        host.Bot.Answers.Should().ContainSingle().Which.Text.Should().Be("This button no longer works.");
        host.Log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("test.unserved"));
    }

    [Fact]
    public async Task Press_ShouldBeAnsweredInThePressersLanguage_AndNotThrow_WhenTheHandlerFails()
    {
        using var host = Create();
        host.Provider.GetRequiredService<PressLog>().Failure = new InvalidOperationException("database down");
        var data = await NoticeAsync(host, TelegramNoticeSenderTests.Button(Revoke, "contact-42"));

        var press = () => PressAsync(host, data[0], from: Owner, language: "ru");

        await press.Should().NotThrowAsync("the button is spent, so a retried update would only be refused");
        host.Bot.Answers.Should().ContainSingle().Which.Text.Should().Be("Что-то пошло не так. Проверьте страницу аккаунта.");
        host.Bot.TextEdits.Should().BeEmpty();
        host.Log.Entries.Should().Contain(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Polling_ShouldHandCallbackQueriesToTheRouter()
    {
        using var host = Create(bot => bot.UpdateMode = TelegramUpdateMode.Polling, environment: "Development");
        var data = await NoticeAsync(host, TelegramNoticeSenderTests.Button(Revoke, "contact-42"));
        host.Bot.Enqueue(Press(data[0], Owner, "q1", "en", updateId: 40));
        var polling = host.Service<TelegramPollingService>();

        await polling.StartAsync(CancellationToken.None);
        await TelegramTestHost.EventuallyAsync(() => !host.Bot.Answers.IsEmpty && host.Bot.PendingCount == 0);
        await polling.StopAsync(CancellationToken.None);

        host.Bot.Answers.Should().ContainSingle().Which.Text.Should().Be("Done");
        host.Bot.Polls.Should().OnlyContain(p => p.AllowedUpdates!.Contains(UpdateType.CallbackQuery));
    }

    private static TelegramTestHost Create(Action<TelegramBotOptions>? bot = null, string environment = "Production")
        => TelegramTestHost.Create(
            bot,
            environment: environment,
            time: new FakeTimeProvider(Now),
            services: services =>
            {
                services.AddSingleton<PressLog>();
                new IdentityBuilder(services).AddTelegramCallbackHandler<RecordingHandler>(Revoke);
            });

    /// <summary>Sends a notice to <see cref="Owner"/> with the buttons, returning each button's callback data.</summary>
    private static async Task<IReadOnlyList<string>> NoticeAsync(TelegramTestHost host, params TelegramNoticeButton[] buttons)
    {
        (await TelegramNoticeSenderTests.SendAsync(host, TelegramNoticeSenderTests.Notice(buttons: buttons))).IsSent.Should().BeTrue();
        var markup = (InlineKeyboardMarkup)host.Bot.Sent.Last().ReplyMarkup!;
        return [.. markup.InlineKeyboard.SelectMany(row => row).Select(button => button.CallbackData!)];
    }

    private static Task PressAsync(TelegramTestHost host, string data, long from, string queryId = "q1", string language = "en")
        => host.HandleAsync(Press(data, from, queryId, language, updateId: 1));

    private static Update Press(string data, long from, string queryId, string language, int updateId)
        => new()
        {
            Id = updateId,
            CallbackQuery = new CallbackQuery
            {
                Id = queryId,
                From = new User { Id = from, IsBot = false, FirstName = "Lola", LanguageCode = language },
                ChatInstance = "chat-instance",
                Data = data,
            },
        };

    /// <summary>What the test handler saw, and what it does next.</summary>
    private sealed class PressLog
    {
        public ConcurrentQueue<TelegramCallback> Calls { get; } = new();

        public TelegramCallbackResult Result { get; set; } = new() { MessageText = "Trust revoked", AnswerText = "Done" };

        public Exception? Failure { get; set; }
    }

    /// <summary>A handler for <see cref="Revoke"/> that records each press.</summary>
    private sealed class RecordingHandler(PressLog log) : ITelegramCallbackHandler
    {
        public Task<TelegramCallbackResult> HandleAsync(TelegramCallback callback, CancellationToken cancellationToken = default)
        {
            log.Calls.Enqueue(callback);
            return log.Failure is { } failure ? Task.FromException<TelegramCallbackResult>(failure) : Task.FromResult(log.Result);
        }
    }
}

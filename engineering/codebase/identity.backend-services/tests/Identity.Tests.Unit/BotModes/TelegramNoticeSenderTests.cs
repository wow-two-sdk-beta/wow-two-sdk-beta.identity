using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.ReplyMarkups;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.BotModes;

/// <summary>
/// The bot's notices: each is recorded due after <see cref="TelegramBotOptions.NoticeLifetime"/>, its buttons carry opaque
/// random callback data stored only as SHA-256, and a newer notice of a kind for an account makes that account's older
/// ones of the kind in the chat due at once.
/// </summary>
public sealed class TelegramNoticeSenderTests
{
    private const string Kind = "contact-sign-in";
    private const long Chat = 501;
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Person = Guid.Parse("01900000-0000-7000-8000-000000000001");
    private static readonly Guid Other = Guid.Parse("01900000-0000-7000-8000-000000000002");

    [Fact]
    public async Task Send_ShouldRecordTheNotice_DueAfterADay_WithOpaqueHashedButtons()
    {
        using var host = TelegramTestHost.Create(time: new FakeTimeProvider(Now));

        var result = await SendAsync(host, Notice(buttons: [Button("test.revoke", "contact-42"), Button("test.keep", payload: null)]));

        result.IsSent.Should().BeTrue();
        var sent = host.Bot.Sent.Should().ContainSingle().Which;
        sent.ChatId.Identifier.Should().Be(Chat);
        var rows = ((InlineKeyboardMarkup)sent.ReplyMarkup!).InlineKeyboard.Select(row => row.ToList()).ToList();
        rows.Should().HaveCount(2).And.OnlyContain(row => row.Count == 1, "one button per row");
        var data = rows.Select(row => row[0].CallbackData!).ToList();
        data.Should().OnlyContain(d => Encoding.UTF8.GetByteCount(d) <= 64).And.OnlyHaveUniqueItems();
        data.Should().NotContain(d => d.Contains("revoke") || d.Contains("contact-42"), "the action and payload never leave the instance");

        var record = host.Ledger.Messages.Should().ContainSingle().Which;
        record.Should().BeEquivalentTo(new
        {
            BotUserName = "ocharo_bot",
            ChatId = Chat,
            MessageId = result.MessageId!.Value,
            Kind,
            UserId = (Guid?)Person,
            Culture = "uz",
            SentAt = Now,
            DeleteAt = Now.AddDays(1),
        });
        var callbacks = host.Ledger.Callbacks;
        callbacks.Select(c => c.TokenHash).Should().Equal(data.Select(Sha256));
        callbacks.Select(c => c.TokenHash).Should().NotIntersectWith(data, "only the digest is stored");
        callbacks.Select(c => (c.Action, c.Payload)).Should().Equal(("test.revoke", "contact-42"), ("test.keep", (string?)null));
        callbacks.Should().OnlyContain(c => c.MessageRecordId == record.Id && c.AllowedTelegramUserId == Chat && c.UsedAt == null);
    }

    [Fact]
    public async Task Send_ShouldTakeTheConfiguredNoticeLifetime_AndSendNoKeyboardWithoutButtons()
    {
        using var host = TelegramTestHost.Create(bot => bot.NoticeLifetime = TimeSpan.FromHours(2), time: new FakeTimeProvider(Now));

        await SendAsync(host, Notice());

        host.Ledger.Messages.Should().ContainSingle().Which.DeleteAt.Should().Be(Now.AddHours(2));
        host.Ledger.Callbacks.Should().BeEmpty();
        host.Bot.Sent.Should().ContainSingle().Which.ReplyMarkup.Should().BeNull();
    }

    [Fact]
    public async Task Send_ShouldMakeTheAccountsOlderNoticesOfTheKindInTheChatDueAtOnce()
    {
        var time = new FakeTimeProvider(Now);
        using var host = TelegramTestHost.Create(time: time);
        await SendAsync(host, Notice());
        await SendAsync(host, Notice(kind: "other-kind"));
        await SendAsync(host, Notice(userId: Other));
        await SendAsync(host, Notice(chatId: 502));
        var first = host.Ledger.Messages.ToList();

        time.Advance(TimeSpan.FromMinutes(10));
        await SendAsync(host, Notice());

        var later = Now.AddMinutes(10);
        host.Ledger.Messages.Single(m => m.Id == first[0].Id).DeleteAt.Should().Be(later, "the older notice of the same kind, account and chat is due at once");
        host.Ledger.Messages.Where(m => first.Skip(1).Any(f => f.Id == m.Id)).Should().OnlyContain(m => m.DeleteAt == Now.AddDays(1), "another kind, account or chat keeps its lifetime");
        host.Ledger.Messages.Should().ContainSingle(m => m.SentAt == later).Which.DeleteAt.Should().Be(later.AddDays(1));

        (await host.SweepAsync()).Should().Be(1);
        host.Bot.Deletions.Should().ContainSingle().Which.MessageIds.Should().Equal(first[0].MessageId);
    }

    [Fact]
    public async Task Send_ShouldReturnTelegramsRefusal_AndChangeNothing()
    {
        using var host = TelegramTestHost.Create(time: new FakeTimeProvider(Now));
        var older = host.Ledger.Seed(Chat, messageId: 9, sentAt: Now.AddHours(-1), deleteAt: Now.AddHours(23), kind: Kind, userId: Person);
        host.Bot.ChatFailures[Chat] = new ApiRequestException("Forbidden: bot was blocked by the user", 403);

        var result = await SendAsync(host, Notice(buttons: [Button("test.revoke", "contact-42")]));

        result.IsSent.Should().BeFalse();
        result.FailureReason.Should().Contain("blocked");
        host.Ledger.Messages.Should().ContainSingle().Which.DeleteAt.Should().Be(older.DeleteAt, "an unsent notice replaces nothing");
        host.Ledger.Callbacks.Should().BeEmpty();
    }

    [Fact]
    public async Task Send_ShouldSendNothing_WhileTheBotIsOff()
    {
        using var host = TelegramTestHost.Create(bot => bot.IsEnabled = false, time: new FakeTimeProvider(Now));

        var result = await SendAsync(host, Notice());

        result.Should().BeEquivalentTo(new TelegramNoticeResult { FailureReason = "bot_disabled" });
        host.Bot.Methods.Should().BeEmpty();
        host.Ledger.Messages.Should().BeEmpty();
    }

    internal static TelegramNotice Notice(string kind = Kind, Guid? userId = null, long chatId = Chat, IReadOnlyList<TelegramNoticeButton>? buttons = null)
        => new()
        {
            Kind = kind,
            UserId = userId ?? Person,
            ChatId = chatId,
            Text = "Your trusted contact Aziz received a sign-in code for Ocharo.",
            Culture = "uz",
            Buttons = buttons ?? [],
        };

    internal static TelegramNoticeButton Button(string action, string? payload, long allowed = Chat)
        => new() { Label = "Revoke trust", Action = action, Payload = payload, AllowedTelegramUserId = allowed };

    internal static async Task<TelegramNoticeResult> SendAsync(TelegramTestHost host, TelegramNotice notice)
    {
        await using var scope = host.Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ITelegramNoticeSender>().SendAsync(notice);
    }

    private static string Sha256(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

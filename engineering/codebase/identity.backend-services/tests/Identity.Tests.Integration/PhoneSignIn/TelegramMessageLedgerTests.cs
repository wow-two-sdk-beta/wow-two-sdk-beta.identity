using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.OneTimeCodes;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;
using WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.PhoneSignIn;

/// <summary>
/// The bot's message ledger over PostgreSQL with a fake bot: a sign-in code is recorded due at its code's expiry and the
/// sweep deletes it then; a newer notice makes the older one due at once and spends its button, the newer one's button
/// works once for its own user, and a swept notice's button no longer works.
/// </summary>
public sealed class TelegramMessageLedgerTests
{
    private const string Phone = "+998901234567";
    private const string Address = "203.0.113.5";
    private const long Chat = 4242;
    private const string HashKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string Revoke = "test.revoke";

    [Fact]
    public async Task CodeMessage_ShouldBeDueAtItsCodesExpiry_AndBeDeletedThen()
    {
        var bot = new FakeTelegramBotClient();
        await using var host = CreateHost(bot);
        await using (var scope = host.Scope())
            await LinkAsync(scope.ServiceProvider);

        await using (var scope = host.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var message = await context.TelegramMessages.AsNoTracking().SingleAsync();
            message.Should().BeEquivalentTo(new { BotUserName = "ocharo_bot", ChatId = Chat, Kind = TelegramMessageKindConstants.Code, UserId = (Guid?)null, SentAt = host.Time.GetUtcNow() });
            (await context.OtpCodes.AsNoTracking().Select(c => c.ExpiresAt).ToListAsync()).Should().Contain(message.DeleteAt, "a code message is cleaned up when its code expires");
            bot.To(Chat).Should().Contain(m => m.Text.Contains(bot.LastCode(Chat)));
        }

        host.Time.Advance(TimeSpan.FromMinutes(4));
        (await SweepAsync(host)).Should().Be(0, "the code still works");
        host.Time.Advance(TimeSpan.FromMinutes(1));
        (await SweepAsync(host)).Should().Be(1);

        bot.Deletions.Should().ContainSingle().Which.ChatId.Identifier.Should().Be(Chat);
        await using var after = host.Scope();
        (await after.ServiceProvider.GetRequiredService<IdentityDbContext>().TelegramMessages.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Notice_ShouldReplaceTheOlderOne_SpendItsButton_ItsButtonWorkOnce_AndASweptButtonNoMore()
    {
        var bot = new FakeTelegramBotClient();
        await using var host = CreateHost(bot);
        var person = Guid.CreateVersion7();
        var first = await NoticeAsync(host, bot, person);
        host.Time.Advance(TimeSpan.FromMinutes(1));
        var second = await NoticeAsync(host, bot, person);

        await using (var scope = host.Scope())
        {
            var rows = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().TelegramMessages.AsNoTracking().OrderBy(m => m.SentAt).ToListAsync();
            rows.Select(m => m.DeleteAt).Should().Equal(host.Time.GetUtcNow(), host.Time.GetUtcNow().AddDays(1));
            (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().TelegramCallbacks.AsNoTracking().Select(c => c.TokenHash).ToListAsync())
                .Should().HaveCount(2).And.NotContain([first, second], "only digests are stored");
        }

        await PressAsync(host, first, "q0");
        bot.Answers.Should().ContainSingle().Which.Text.Should().Be("This button no longer works.", "a replaced notice's button is spent at once");
        PressCount.Of(host).Should().Be(0);

        await PressAsync(host, second, "q1");
        await PressAsync(host, second, "q2");
        bot.Answers.Skip(1).Select(a => a.Text).Should().Equal("Done", "This button no longer works.");
        bot.TextEdits.Should().ContainSingle().Which.Text.Should().Be("Trust revoked");
        PressCount.Of(host).Should().Be(1);

        (await SweepAsync(host)).Should().Be(1, "the replaced notice is due");
        bot.Deletions.Should().ContainSingle();
        await PressAsync(host, first, "q3");
        bot.Answers.Last().Text.Should().Be("This button no longer works.", "a swept notice's button is gone with it");
        PressCount.Of(host).Should().Be(1);
    }

    /// <summary>Links <see cref="Phone"/> to <see cref="Chat"/> through the bot, which sends the first code.</summary>
    private static async Task LinkAsync(IServiceProvider services)
    {
        var url = (await services.GetRequiredService<UserPhoneSignInService>().SendAsync(Phone, Address)).TelegramLinkUrl!;
        var updates = services.GetRequiredService<TelegramUpdateHandler>();
        await updates.HandleAsync(Update(message => message.Text = "/start " + url[(url.IndexOf('=') + 1)..]));
        await updates.HandleAsync(Update(message => message.Contact = new Contact { PhoneNumber = Phone, FirstName = "Lola", UserId = Chat }));
    }

    /// <summary>Sends a notice with one revoke button to <see cref="Chat"/>, returning the button's callback data.</summary>
    private static async Task<string> NoticeAsync(AccountsHost host, FakeTelegramBotClient bot, Guid person)
    {
        await using var scope = host.Scope();
        var result = await scope.ServiceProvider.GetRequiredService<ITelegramNoticeSender>().SendAsync(new TelegramNotice
        {
            Kind = "contact-sign-in",
            UserId = person,
            ChatId = Chat,
            Text = "Aziz received a sign-in code for your account.",
            Buttons = [new TelegramNoticeButton { Label = "Revoke trust", Action = Revoke, Payload = "contact-42", AllowedTelegramUserId = Chat }],
        });
        result.IsSent.Should().BeTrue();
        return ((InlineKeyboardMarkup)bot.Sent.Last().ReplyMarkup!).InlineKeyboard.Single().Single().CallbackData!;
    }

    private static async Task PressAsync(AccountsHost host, string data, string queryId)
    {
        await using var scope = host.Scope();
        await scope.ServiceProvider.GetRequiredService<TelegramUpdateHandler>().HandleAsync(new Update
        {
            Id = 2,
            CallbackQuery = new CallbackQuery
            {
                Id = queryId,
                From = new User { Id = Chat, IsBot = false, FirstName = "Lola", LanguageCode = "en" },
                ChatInstance = "chat-instance",
                Data = data,
            },
        });
    }

    private static async Task<int> SweepAsync(AccountsHost host)
    {
        await using var scope = host.Scope();
        return await scope.ServiceProvider.GetRequiredService<TelegramMessageSweeper>().SweepAsync();
    }

    private static AccountsHost CreateHost(FakeTelegramBotClient bot) => AccountsHost.Create(
        identity => identity
            .AddSignIn()
            .AddDatabaseOtpStore()
            .AddPhoneSignIn(
                telegram =>
                {
                    telegram.IsEnabled = true;
                    telegram.BotToken = "123456:TEST-token";
                    telegram.BotUserName = "Ocharo_Bot";
                    telegram.WebhookSecret = "hook-secret";
                },
                phones => phones.AllowedCountries = ["UZ"])
            .AddTelegramCallbackHandler<RevokeHandler>(Revoke),
        services: services =>
        {
            services.AddOtpService(o =>
            {
                o.HashKey = HashKey;
                o.Messages.AppName = "Ocharo";
            });
            services.AddSingleton<PressCount>();
            services.RemoveAll<ITelegramBotClient>();
            services.AddSingleton<ITelegramBotClient>(bot);
        });

    private static Update Update(Action<Message> shape)
    {
        var message = new Message
        {
            Id = 1,
            Date = DateTime.UtcNow,
            Chat = new Chat { Id = Chat, Type = ChatType.Private },
            From = new User { Id = Chat, IsBot = false, FirstName = "Lola", LanguageCode = "uz" },
        };
        shape(message);
        return new Update { Id = 1, Message = message };
    }

    /// <summary>How many presses reached the test handler.</summary>
    private sealed class PressCount
    {
        private int _count;

        public static int Of(AccountsHost host) => host.Provider.GetRequiredService<PressCount>()._count;

        public void Add() => Interlocked.Increment(ref _count);
    }

    /// <summary>A stand-in for trusted contacts' revoke: it counts the press and words the edited notice.</summary>
    private sealed class RevokeHandler(PressCount count) : ITelegramCallbackHandler
    {
        public Task<TelegramCallbackResult> HandleAsync(TelegramCallback callback, CancellationToken cancellationToken = default)
        {
            count.Add();
            return Task.FromResult(new TelegramCallbackResult { MessageText = "Trust revoked", AnswerText = "Done" });
        }
    }
}

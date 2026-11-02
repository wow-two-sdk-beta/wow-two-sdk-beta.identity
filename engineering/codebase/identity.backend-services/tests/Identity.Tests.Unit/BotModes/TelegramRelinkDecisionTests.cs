using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.BotModes;

/// <summary>
/// The shared contact's decision when its number already links to a chat: a number a person holds confirmed stays with the
/// Telegram account it was linked from and waits for approval, while the same account or a number nobody holds links as
/// before; and the pending change's approval, denial and one-day life.
/// </summary>
public sealed class TelegramRelinkDecisionTests
{
    private const string Phone = "+998901234567";
    private const string Bot = "ocharo_bot";
    private const long Owner = 1001;
    private const long Newcomer = 2002;

    [Fact]
    public async Task RecycledNumber_ShouldKeepItsLink_RecordAPendingChange_AndCompleteNothing()
    {
        using var test = Create();
        test.Host.Users.Confirm(Phone);
        await LinkAsync(test.Host, Owner);
        await OpenAsync(test, Newcomer, Phone);

        await HandleAsync(test.Host, Contact(Newcomer, Phone));

        test.Host.Links.Links.Should().ContainSingle().Which.Should().BeEquivalentTo(new { ChatId = Owner, TelegramUserId = Owner }, "the old person's chat keeps the codes");
        test.Host.Links.Changes.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { BotUserName = Bot, PhoneNumber = Phone, ChatId = Newcomer, TelegramUserId = Newcomer, RequestedAt = test.Time.GetUtcNow() });
        test.Completed.Should().BeEmpty("no code is sent and nobody signs up");
        test.Host.Links.Requests.Single().ConsumedAt.Should().NotBeNull("the start link is spent");
        var reply = test.Host.Bot.Sent.Should().ContainSingle().Subject;
        reply.ChatId.Identifier.Should().Be(Newcomer);
        reply.Text.Should().Contain("linked to another Telegram account").And.Contain("admin of your organization");
        reply.ReplyMarkup.Should().BeOfType<ReplyKeyboardRemove>();
    }

    [Fact]
    public async Task SameTelegramUser_ShouldRelink_AndComplete()
    {
        using var test = Create();
        test.Host.Users.Confirm(Phone);
        await LinkAsync(test.Host, Owner);
        await OpenAsync(test, Owner, Phone);

        await HandleAsync(test.Host, Contact(Owner, Phone));

        test.Host.Links.Links.Should().ContainSingle().Which.LinkedAt.Should().Be(test.Time.GetUtcNow());
        test.Host.Links.Changes.Should().BeEmpty();
        test.Completed.Should().ContainSingle().Which.Should().BeEquivalentTo(new { PhoneNumber = Phone, ChatId = Owner, TelegramUserId = Owner });
    }

    [Fact]
    public async Task NumberNobodyHolds_ShouldMoveToTheNewAccount_AsBefore()
    {
        using var test = Create();
        await LinkAsync(test.Host, Owner);
        await OpenAsync(test, Newcomer, Phone);

        await HandleAsync(test.Host, Contact(Newcomer, Phone));

        test.Host.Links.Links.Should().ContainSingle().Which.Should().BeEquivalentTo(new { ChatId = Newcomer, TelegramUserId = Newcomer });
        test.Host.Links.Changes.Should().BeEmpty();
        test.Completed.Should().ContainSingle().Which.ChatId.Should().Be(Newcomer);
    }

    [Fact]
    public async Task HeldNumberWithoutALink_ShouldLink_AsBefore()
    {
        using var test = Create();
        test.Host.Users.Confirm(Phone);
        await OpenAsync(test, Newcomer, Phone);

        await HandleAsync(test.Host, Contact(Newcomer, Phone));

        test.Host.Links.Links.Should().ContainSingle().Which.TelegramUserId.Should().Be(Newcomer, "only a number linked to another account waits");
        test.Completed.Should().ContainSingle();
    }

    [Fact]
    public async Task LaterRequest_ShouldReplaceTheEarlierOne()
    {
        using var test = Create();
        test.Host.Users.Confirm(Phone);
        await LinkAsync(test.Host, Owner);
        await OpenAsync(test, Newcomer, Phone);
        await HandleAsync(test.Host, Contact(Newcomer, Phone));
        test.Time.Advance(TimeSpan.FromMinutes(5));
        await OpenAsync(test, 3003, Phone);

        await HandleAsync(test.Host, Contact(3003, Phone));

        test.Host.Links.Changes.Should().ContainSingle().Which.Should().BeEquivalentTo(new { ChatId = 3003L, TelegramUserId = 3003L, RequestedAt = test.Time.GetUtcNow() });
    }

    [Fact]
    public async Task Approve_ShouldMoveTheLinkToTheAskingAccount_Once()
    {
        using var test = Create();
        await PendingAsync(test);

        await using var scope = test.Host.Provider.CreateAsyncScope();
        var changes = scope.ServiceProvider.GetRequiredService<TelegramLinkChangeService>();
        (await changes.IsPendingAsync(Phone)).Should().BeTrue();
        (await changes.FindPendingNumbersAsync([Phone, "+998907654321"])).Should().Equal(Phone);

        (await changes.ApproveAsync(Phone)).Should().BeTrue();

        test.Host.Links.Links.Should().ContainSingle().Which.Should().BeEquivalentTo(new { ChatId = Newcomer, TelegramUserId = Newcomer, LinkedAt = test.Time.GetUtcNow() });
        test.Host.Links.Changes.Should().BeEmpty();
        (await changes.ApproveAsync(Phone)).Should().BeFalse("the change was spent");
        (await changes.DenyAsync(Phone)).Should().BeFalse();
    }

    [Fact]
    public async Task Deny_ShouldDropTheChange_AndKeepTheLink()
    {
        using var test = Create();
        await PendingAsync(test);

        await using var scope = test.Host.Provider.CreateAsyncScope();
        var changes = scope.ServiceProvider.GetRequiredService<TelegramLinkChangeService>();
        (await changes.DenyAsync(Phone)).Should().BeTrue();

        test.Host.Links.Links.Should().ContainSingle().Which.TelegramUserId.Should().Be(Owner);
        test.Host.Links.Changes.Should().BeEmpty();
        (await changes.IsPendingAsync(Phone)).Should().BeFalse();
        (await changes.ApproveAsync(Phone)).Should().BeFalse();
    }

    [Fact]
    public async Task Change_ShouldExpireAfterADay_AndBePrunedByTheNextOne()
    {
        using var test = Create();
        await PendingAsync(test);
        test.Time.Advance(TelegramLinkChangeService.Lifetime + TimeSpan.FromSeconds(1));

        await using (var scope = test.Host.Provider.CreateAsyncScope())
        {
            var changes = scope.ServiceProvider.GetRequiredService<TelegramLinkChangeService>();
            (await changes.IsPendingAsync(Phone)).Should().BeFalse("a day-old change no longer counts");
            (await changes.FindPendingNumbersAsync([Phone])).Should().BeEmpty();
            (await changes.ApproveAsync(Phone)).Should().BeFalse();
        }

        test.Host.Users.Confirm("+998907654321");
        await test.Host.Links.SaveLinkAsync(Link("+998907654321", Owner));
        await OpenAsync(test, Newcomer, "+998907654321");
        await HandleAsync(test.Host, Contact(Newcomer, "+998907654321"));

        test.Host.Links.Changes.Should().ContainSingle().Which.PhoneNumber.Should().Be("+998907654321", "recording a change prunes the expired one");
        test.Host.Links.Links.Should().Contain(l => l.PhoneNumber == Phone && l.TelegramUserId == Owner);
    }

    [Fact]
    public async Task DeletedAccount_ShouldTakeItsPendingChangeWithItsLinks()
    {
        using var test = Create();
        await PendingAsync(test);

        await test.Host.Links.DeleteLinksAsync(Phone);

        test.Host.Links.Links.Should().BeEmpty();
        test.Host.Links.Changes.Should().BeEmpty();
    }

    /// <summary>A recycled number's newcomer has asked for the old person's linked number.</summary>
    private static async Task PendingAsync(RelinkTest test)
    {
        test.Host.Users.Confirm(Phone);
        await LinkAsync(test.Host, Owner);
        await OpenAsync(test, Newcomer, Phone);
        await HandleAsync(test.Host, Contact(Newcomer, Phone));
        test.Host.Links.Changes.Should().ContainSingle();
    }

    private static RelinkTest Create()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero));
        var completed = new List<TelegramLinkCompletion>();
        var host = TelegramTestHost.Create(
            time: time,
            services: services => services.AddScoped<ITelegramLinkCompletionHandler>(_ => new RecordingHandler(completed)));
        return new RelinkTest(host, time, completed);
    }

    private static Task LinkAsync(TelegramTestHost host, long telegramUserId) => host.Links.SaveLinkAsync(Link(Phone, telegramUserId));

    private static IdentityTelegramLink Link(string phone, long telegramUserId) => new()
    {
        Id = Guid.NewGuid(),
        BotUserName = Bot,
        PhoneNumber = phone,
        ChatId = telegramUserId,
        TelegramUserId = telegramUserId,
        LinkedAt = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero),
    };

    /// <summary>A start link for <paramref name="phone"/> that <paramref name="telegramUserId"/> opened in their chat.</summary>
    private static Task OpenAsync(RelinkTest test, long telegramUserId, string phone)
    {
        var now = test.Time.GetUtcNow();
        return test.Host.Links.AddRequestAsync(new IdentityTelegramLinkRequest
        {
            Id = Guid.CreateVersion7(now),
            TokenHash = Guid.NewGuid().ToString("N"),
            Purpose = TelegramLinkPurposeConstants.PhoneSignIn,
            BotUserName = Bot,
            PhoneNumber = phone,
            ChatId = telegramUserId,
            TelegramUserId = telegramUserId,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(15),
        });
    }

    private static async Task HandleAsync(TelegramTestHost host, Update update)
    {
        await using var scope = host.Provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TelegramUpdateHandler>().HandleAsync(update);
    }

    private static Update Contact(long telegramUserId, string phone) => new()
    {
        Id = 1,
        Message = new Message
        {
            Id = 1,
            Date = DateTime.UtcNow,
            Chat = new Chat { Id = telegramUserId, Type = ChatType.Private },
            From = new User { Id = telegramUserId, IsBot = false, FirstName = "Lola", LanguageCode = "en" },
            Contact = new Contact { PhoneNumber = phone, FirstName = "Lola", UserId = telegramUserId },
        },
    };

    private sealed record RelinkTest(TelegramTestHost Host, FakeTimeProvider Time, List<TelegramLinkCompletion> Completed) : IDisposable
    {
        public void Dispose() => Host.Dispose();
    }

    /// <summary>Records each completed phone sign-in link instead of sending a code.</summary>
    private sealed class RecordingHandler(List<TelegramLinkCompletion> completed) : ITelegramLinkCompletionHandler
    {
        public string Purpose => TelegramLinkPurposeConstants.PhoneSignIn;

        public Task HandleAsync(TelegramLinkCompletion completion, CancellationToken cancellationToken = default)
        {
            lock (completed)
                completed.Add(completion);
            return Task.CompletedTask;
        }
    }
}

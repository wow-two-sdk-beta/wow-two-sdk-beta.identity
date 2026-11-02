using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.OneTimeCodes;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;
using WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.PhoneSignIn;

/// <summary>
/// A linked number shared again from another Telegram account, over PostgreSQL with a fake bot: a recycled number a person
/// holds confirmed keeps its link and waits for approval, the same account or a number nobody holds relinks, and
/// approving moves the link to the asking chat while denying drops the request.
/// </summary>
public sealed class TelegramRelinkTests
{
    private const string Phone = "+998901234567";
    private const string Address = "203.0.113.5";
    private const long OldChat = 4242;
    private const long NewChat = 5353;
    private const string HashKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    [Fact]
    public async Task RecycledNumber_ShouldKeepTheHoldersLink_SendNoCode_AndWaitForApproval()
    {
        var bot = new FakeTelegramBotClient();
        await using var host = CreateHost(bot);
        await HolderAsync(host, bot);

        // The old account is gone, so the code cannot reach it and sign-in falls back to a start link the newcomer opens.
        bot.BlockedChats[OldChat] = true;
        host.Time.Advance(TimeSpan.FromSeconds(31));
        await using var scope = host.Scope();
        var services = scope.ServiceProvider;
        var url = (await services.GetRequiredService<UserPhoneSignInService>().SendAsync(Phone, Address)).TelegramLinkUrl!;
        var updates = services.GetRequiredService<TelegramUpdateHandler>();
        await updates.HandleAsync(Start(url, NewChat));
        await updates.HandleAsync(Contact(Phone, NewChat));

        bot.To(NewChat).Last().Text.Should().Contain("boshqa Telegram akkauntiga", "the bot says the number belongs to another account");
        bot.To(NewChat).Should().NotContain(m => m.Text.Contains("tasdiqlash kodingiz"), "no sign-in code reaches the newcomer");
        var link = (await services.GetRequiredService<ITelegramLinkRepository>().FindLinkAsync("ocharo_bot", Phone))!;
        link.Should().BeEquivalentTo(new { ChatId = OldChat, TelegramUserId = OldChat });
        var change = await services.GetRequiredService<IdentityDbContext>().TelegramLinkChanges.AsNoTracking().SingleAsync();
        change.Should().BeEquivalentTo(new { BotUserName = "ocharo_bot", PhoneNumber = Phone, ChatId = NewChat, TelegramUserId = NewChat, RequestedAt = host.Time.GetUtcNow() });
        (await services.GetRequiredService<TelegramLinkChangeService>().IsPendingAsync(Phone)).Should().BeTrue();
        (await services.GetRequiredService<IdentityDbContext>().TelegramLinkRequests.AsNoTracking().AllAsync(r => r.ConsumedAt != null)).Should().BeTrue("the start link is spent");
    }

    [Fact]
    public async Task SameTelegramAccount_ShouldRelink_AndGetItsCode()
    {
        var bot = new FakeTelegramBotClient();
        await using var host = CreateHost(bot);
        await HolderAsync(host, bot);
        var first = bot.LastCode(OldChat);

        await using var scope = host.Scope();
        var services = scope.ServiceProvider;
        var url = await services.GetRequiredService<TelegramLinkService>().CreateStartLinkAsync(Phone, TelegramLinkPurposeConstants.PhoneSignIn, origin: null);
        host.Time.Advance(TimeSpan.FromSeconds(31));
        var updates = services.GetRequiredService<TelegramUpdateHandler>();
        await updates.HandleAsync(Start(url, OldChat));
        await updates.HandleAsync(Contact(Phone, OldChat));

        bot.LastCode(OldChat).Should().NotBe(first, "the relinked chat gets a new code");
        (await services.GetRequiredService<ITelegramLinkRepository>().FindLinkAsync("ocharo_bot", Phone))!.LinkedAt.Should().Be(host.Time.GetUtcNow());
        (await services.GetRequiredService<IdentityDbContext>().TelegramLinkChanges.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task NumberNobodyHolds_ShouldMoveToTheNewAccount()
    {
        var bot = new FakeTelegramBotClient();
        await using var host = CreateHost(bot);
        await using var scope = host.Scope();
        var services = scope.ServiceProvider;
        await LinkAsync(services, OldChat);

        var url = await services.GetRequiredService<TelegramLinkService>().CreateStartLinkAsync(Phone, TelegramLinkPurposeConstants.PhoneSignIn, origin: null);
        host.Time.Advance(TimeSpan.FromSeconds(31));
        var updates = services.GetRequiredService<TelegramUpdateHandler>();
        await updates.HandleAsync(Start(url, NewChat));
        await updates.HandleAsync(Contact(Phone, NewChat));

        bot.LastCode(NewChat).Should().HaveLength(6);
        (await services.GetRequiredService<ITelegramLinkRepository>().FindLinkAsync("ocharo_bot", Phone))!.TelegramUserId.Should().Be(NewChat);
        (await services.GetRequiredService<IdentityDbContext>().TelegramLinkChanges.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Approve_ShouldMoveTheLinkToTheAskingChat_SoTheNextCodeGoesThere()
    {
        var bot = new FakeTelegramBotClient();
        await using var host = CreateHost(bot);
        var person = await HolderAsync(host, bot);
        await PendingAsync(host);
        var stamp = (await host.ReloadAsync(person.Id)).SecurityStamp;

        await using var scope = host.Scope();
        var services = scope.ServiceProvider;
        (await services.GetRequiredService<TelegramLinkChangeService>().ApproveAsync(Phone)).Should().BeTrue();

        (await services.GetRequiredService<ITelegramLinkRepository>().FindLinkAsync("ocharo_bot", Phone))!.Should().BeEquivalentTo(new { ChatId = NewChat, TelegramUserId = NewChat });
        (await services.GetRequiredService<IdentityDbContext>().TelegramLinkChanges.AnyAsync()).Should().BeFalse();
        (await host.ReloadAsync(person.Id)).SecurityStamp.Should().Be(stamp, "approval rotates nothing else");
        (await services.GetRequiredService<TelegramLinkChangeService>().ApproveAsync(Phone)).Should().BeFalse("it was spent");

        host.Time.Advance(TimeSpan.FromSeconds(31));
        (await services.GetRequiredService<UserPhoneSignInService>().SendAsync(Phone, Address)).Status.Should().Be(PhoneSignInStatus.CodeSent);
        bot.LastCode(NewChat).Should().HaveLength(6);
    }

    [Fact]
    public async Task Deny_ShouldDropTheRequest_AndKeepTheHoldersLink()
    {
        var bot = new FakeTelegramBotClient();
        await using var host = CreateHost(bot);
        await HolderAsync(host, bot);
        await PendingAsync(host);

        await using var scope = host.Scope();
        var services = scope.ServiceProvider;
        (await services.GetRequiredService<TelegramLinkChangeService>().DenyAsync(Phone)).Should().BeTrue();

        (await services.GetRequiredService<ITelegramLinkRepository>().FindLinkAsync("ocharo_bot", Phone))!.TelegramUserId.Should().Be(OldChat);
        (await services.GetRequiredService<IdentityDbContext>().TelegramLinkChanges.AnyAsync()).Should().BeFalse();
        (await services.GetRequiredService<TelegramLinkChangeService>().DenyAsync(Phone)).Should().BeFalse();
        (await services.GetRequiredService<TelegramLinkChangeService>().ApproveAsync(Phone)).Should().BeFalse();
    }

    [Fact]
    public async Task PendingChange_ShouldLapseAfterADay()
    {
        var bot = new FakeTelegramBotClient();
        await using var host = CreateHost(bot);
        await HolderAsync(host, bot);
        await PendingAsync(host);

        host.Time.Advance(TelegramLinkChangeService.Lifetime + TimeSpan.FromSeconds(1));

        await using var scope = host.Scope();
        var changes = scope.ServiceProvider.GetRequiredService<TelegramLinkChangeService>();
        (await changes.IsPendingAsync(Phone)).Should().BeFalse();
        (await changes.FindPendingNumbersAsync([Phone])).Should().BeEmpty();
        (await changes.ApproveAsync(Phone)).Should().BeFalse();
    }

    /// <summary>Makes a person holding <see cref="Phone"/> confirmed, linked to <see cref="OldChat"/> through the bot.</summary>
    private static async Task<IdentityUser> HolderAsync(AccountsHost host, FakeTelegramBotClient bot)
    {
        var person = await host.CreateUserAsync("lola");
        await using var scope = host.Scope();
        var services = scope.ServiceProvider;
        var accounts = services.GetRequiredService<UserAccountService>();
        var tracked = (await accounts.FindByIdAsync(person.Id))!;
        tracked.PhoneNumber = Phone;
        tracked.IsPhoneNumberConfirmed = true;
        await accounts.UpdateAsync(tracked);
        await LinkAsync(services, OldChat);
        bot.LastCode(OldChat).Should().HaveLength(6);
        return person;
    }

    /// <summary>A newcomer in <see cref="NewChat"/> shares the holder's number from a start link.</summary>
    private static async Task PendingAsync(AccountsHost host)
    {
        await using var scope = host.Scope();
        var services = scope.ServiceProvider;
        var url = await services.GetRequiredService<TelegramLinkService>().CreateStartLinkAsync(Phone, TelegramLinkPurposeConstants.PhoneSignIn, origin: null);
        var updates = services.GetRequiredService<TelegramUpdateHandler>();
        await updates.HandleAsync(Start(url, NewChat));
        await updates.HandleAsync(Contact(Phone, NewChat));
        (await services.GetRequiredService<TelegramLinkChangeService>().IsPendingAsync(Phone)).Should().BeTrue();
    }

    /// <summary>Links <see cref="Phone"/> to <paramref name="chatId"/> through the bot, as a person would.</summary>
    private static async Task LinkAsync(IServiceProvider services, long chatId)
    {
        var url = (await services.GetRequiredService<UserPhoneSignInService>().SendAsync(Phone, Address)).TelegramLinkUrl!;
        var updates = services.GetRequiredService<TelegramUpdateHandler>();
        await updates.HandleAsync(Start(url, chatId));
        await updates.HandleAsync(Contact(Phone, chatId));
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
                phones => phones.AllowedCountries = ["UZ"]),
        services: services =>
        {
            services.AddOtpService(o =>
            {
                o.HashKey = HashKey;
                o.Messages.AppName = "Ocharo";
            });
            services.RemoveAll<ITelegramBotClient>();
            services.AddSingleton<ITelegramBotClient>(bot);
        });

    private static Update Start(string url, long chatId) => Update(chatId, message => message.Text = "/start " + url[(url.IndexOf('=') + 1)..]);

    private static Update Contact(string phone, long chatId)
        => Update(chatId, message => message.Contact = new Contact { PhoneNumber = phone, FirstName = "Lola", UserId = chatId });

    private static Update Update(long chatId, Action<Message> shape)
    {
        var message = new Message
        {
            Id = 1,
            Date = DateTime.UtcNow,
            Chat = new Chat { Id = chatId, Type = ChatType.Private },
            From = new User { Id = chatId, IsBot = false, FirstName = "Lola", LanguageCode = "uz" },
        };
        shape(message);
        return new Update { Id = 1, Message = message };
    }
}

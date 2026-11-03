using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Telegram.Bot;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.OneTimeCodes;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;
using WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.PhoneSignIn;

/// <summary>
/// <see cref="TrustedContactService"/> and <see cref="OwnPhoneService"/> over PostgreSQL with a fake bot: one contact
/// trusted by two accounts shares one 30 s window and one failure budget across both, a code bound to one account signs in
/// no other, the cap and the self-contact are refused, and an <c>add-phone</c> link refuses a taken number and a stale
/// change.
/// </summary>
public sealed class TrustedContactTests
{
    private const string HashKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string ContactPhone = "+998907777777";
    private const long ContactChat = 8101;

    [Fact]
    public async Task OneContact_ShouldShareItsWindowAndBudget_AcrossTheAccountsTrustingIt_AndBindEachCodeToItsAccount()
    {
        var bot = new FakeTelegramBotClient();
        await using var host = CreateHost(bot);
        var contact = await PersonAsync(host, "contact", ContactPhone, ContactChat);
        var lola = await PersonAsync(host, "lola", "+998901111111", chatId: null);
        var aziz = await PersonAsync(host, "aziz", "+998902222222", chatId: null);
        await using (var scope = host.Scope())
        {
            var trusted = scope.ServiceProvider.GetRequiredService<TrustedContactService>();
            (await trusted.RecordAsync(lola.Id, contact.Id, contact.Id)).IsSuccess.Should().BeTrue();
            (await trusted.RecordAsync(aziz.Id, contact.Id, contact.Id)).IsSuccess.Should().BeTrue();
        }

        (await SendAsync(host, lola)).Should().Be(TrustedContactSendStatus.CodeSent);
        var lolaCode = bot.LastCode(ContactChat);
        (await SendAsync(host, aziz)).Should().Be(TrustedContactSendStatus.RateLimited, "the contact received a code moments ago, whichever account it was for");

        await using (var scope = host.Scope())
        {
            var trusted = scope.ServiceProvider.GetRequiredService<TrustedContactService>();
            (await trusted.VerifySignInCodeAsync(aziz, lolaCode)).Should().BeNull("a code bound to one account signs in no other");
            (await trusted.VerifySignInCodeAsync(lola, lolaCode)).Should().Be(contact.Id);
        }

        // Ten wrong tries on the contact's codes, for either account, spend the contact's budget for both.
        for (var round = 0; round < 5; round++)
        {
            host.Time.Advance(TimeSpan.FromSeconds(31));
            (await SendAsync(host, round % 2 == 0 ? aziz : lola)).Should().Be(TrustedContactSendStatus.CodeSent);
            var code = bot.LastCode(ContactChat);
            await using var scope = host.Scope();
            var trusted = scope.ServiceProvider.GetRequiredService<TrustedContactService>();
            (await trusted.VerifySignInCodeAsync(aziz, Wrong(code))).Should().BeNull();
            (await trusted.VerifySignInCodeAsync(lola, Wrong(code))).Should().BeNull();
        }

        host.Time.Advance(TimeSpan.FromSeconds(31));
        (await SendAsync(host, lola)).Should().Be(TrustedContactSendStatus.RateLimited, "the contact spent their failure budget");
        (await SendAsync(host, aziz)).Should().Be(TrustedContactSendStatus.RateLimited);
    }

    [Fact]
    public async Task Record_ShouldRefuseOneselfAndTheCap_AndRecordAPairOnce()
    {
        var bot = new FakeTelegramBotClient();
        await using var host = CreateHost(bot, maxPerPerson: 2);
        var lola = await PersonAsync(host, "lola", "+998901111111", chatId: null);
        var first = await PersonAsync(host, "first", "+998903333331", chatId: null);
        var second = await PersonAsync(host, "second", "+998903333332", chatId: null);
        var third = await PersonAsync(host, "third", "+998903333333", chatId: null);
        await using var scope = host.Scope();
        var trusted = scope.ServiceProvider.GetRequiredService<TrustedContactService>();

        (await trusted.RecordAsync(lola.Id, lola.Id, lola.Id)).Errors.Single().Code.Should().Be(TrustedContactErrorCodeConstants.InvalidTrustedContact);
        (await trusted.RecordAsync(lola.Id, first.Id, first.Id)).IsSuccess.Should().BeTrue();
        (await trusted.RecordAsync(lola.Id, first.Id, first.Id)).IsSuccess.Should().BeTrue("recording a pair again changes nothing");
        (await trusted.RecordAsync(lola.Id, second.Id, first.Id)).IsSuccess.Should().BeTrue();
        (await trusted.RecordAsync(lola.Id, third.Id, first.Id)).Errors.Single().Code.Should().Be(TrustedContactErrorCodeConstants.TooManyTrustedContacts);
        (await trusted.ListContactsAsync(lola)).Select(c => c.UserId).Should().Equal(first.Id, second.Id);
        (await trusted.ListContactsAsync(lola)).Should().AllSatisfy(c => c.IsDeliverable.Should().BeFalse("no contact's phone is linked to the bot"));
    }

    [Fact]
    public async Task AddPhoneLink_ShouldRefuseATakenNumber_AndAChangeTheLinkWasNotMadeFor()
    {
        var bot = new FakeTelegramBotClient();
        await using var host = CreateHost(bot);
        var holder = await PersonAsync(host, "holder", "+998904444444", chatId: null);
        var phoneless = await host.CreateUserAsync("phoneless");
        await using (var scope = host.Scope())
        {
            var phones = scope.ServiceProvider.GetRequiredService<OwnPhoneService>();
            var link = await phones.CreateLinkAsync(phoneless, sessionId: null, origin: null);
            link.Status.Should().Be(OwnPhoneLinkStatus.Created, "an account with no phone adds one from any session");
            link.TelegramLinkUrl.Should().StartWith("https://t.me/Ocharo_Bot?start=");
            (await phones.CompleteAsync(phoneless.Id, holder.PhoneNumber!)).Should().Be(TelegramBotMessageKeyConstants.AddPhoneTaken);
            (await phones.CompleteAsync(phoneless.Id, "+998905555555")).Should().Be(TelegramBotMessageKeyConstants.AddPhoneDone);
        }

        var added = await host.ReloadAsync(phoneless.Id);
        (added.PhoneNumber, added.IsPhoneNumberConfirmed).Should().Be(("+998905555555", true));
        await using var again = host.Scope();
        var own = again.ServiceProvider.GetRequiredService<OwnPhoneService>();
        (await own.CreateLinkAsync(added, sessionId: null, origin: null)).Status.Should().Be(OwnPhoneLinkStatus.MainPhoneRequired, "a token without an own-phone session changes no phone");
        (await own.CompleteAsync(added.Id, "+998906666666")).Should().Be(TelegramBotMessageKeyConstants.AddPhoneFailed, "a link made to add a phone changes none once one is held");
        (await host.ReloadAsync(added.Id)).PhoneNumber.Should().Be("+998905555555");
    }

    [Fact]
    public async Task PersonWithNeitherEmailNorPhone_ShouldMeetTheConfirmedEmailPrecondition_ThroughTheContact()
    {
        await using var host = AccountsHost.Create(identity => identity.AddSignIn(), options => options.SignIn.IsConfirmedEmailRequired = true);
        await using var scope = host.Scope();
        var person = new IdentityUser { UserName = "member-" + Guid.NewGuid().ToString("N"), DisplayName = "Maftuna" };
        (await scope.ServiceProvider.GetRequiredService<UserAccountService>().CreateAsync(person)).IsSuccess.Should().BeTrue();
        var unconfirmed = new IdentityUser { UserName = "lola", PhoneNumber = "+998901111111" };
        (await scope.ServiceProvider.GetRequiredService<UserAccountService>().CreateAsync(unconfirmed)).IsSuccess.Should().BeTrue();
        var signIn = scope.ServiceProvider.GetRequiredService<SignInService>();

        (await signIn.SignInAsync(person, ["otp"], "trusted_contact_code")).IsSuccess.Should().BeTrue("the contact who signs it in proves the person");
        (await signIn.SignInAsync(unconfirmed, ["otp"], "phone_code")).IsSuccess.Should().BeFalse("an unconfirmed phone proves nothing");
    }

    private static AccountsHost CreateHost(FakeTelegramBotClient bot, int maxPerPerson = 3) => AccountsHost.Create(
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
            .AddTrustedContacts(o =>
            {
                o.IsEnabled = true;
                o.MaxPerPerson = maxPerPerson;
            }),
        services: services =>
        {
            services.AddOtpService(o => o.HashKey = HashKey);
            services.RemoveAll<ITelegramBotClient>();
            services.AddSingleton<ITelegramBotClient>(bot);
        });

    /// <summary>A person holding <paramref name="phone"/> confirmed, linked to <paramref name="chatId"/> on the bot unless it is null.</summary>
    private static async Task<IdentityUser> PersonAsync(AccountsHost host, string userName, string phone, long? chatId)
    {
        await using var scope = host.Scope();
        var person = new IdentityUser { UserName = userName, DisplayName = userName, PhoneNumber = phone, IsPhoneNumberConfirmed = true };
        (await scope.ServiceProvider.GetRequiredService<UserAccountService>().CreateAsync(person)).IsSuccess.Should().BeTrue();
        if (chatId is { } chat)
        {
            await scope.ServiceProvider.GetRequiredService<ITelegramLinkRepository>().SaveLinkAsync(new IdentityTelegramLink
            {
                Id = Guid.CreateVersion7(),
                BotUserName = "ocharo_bot",
                PhoneNumber = phone,
                ChatId = chat,
                TelegramUserId = chat,
                LinkedAt = host.Time.GetUtcNow(),
            });
        }

        return person;
    }

    /// <summary>Sends <paramref name="account"/>'s sign-in code to its one contact, as <c>phone-sign-in/send</c> with a target does.</summary>
    private static async Task<TrustedContactSendStatus> SendAsync(AccountsHost host, IdentityUser account)
    {
        await using var scope = host.Scope();
        var trusted = scope.ServiceProvider.GetRequiredService<TrustedContactService>();
        var target = (await trusted.ListTargetsAsync(account.PhoneNumber)).Single();
        return await trusted.SendSignInCodeAsync(account.PhoneNumber, target.Id, origin: null);
    }

    private static string Wrong(string code) => code == "000000" ? "111111" : "000000";
}

using AwesomeAssertions;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Lockout;
using WoW.Two.Sdk.Beta.Be.Identity.Core.OneTimeCodes;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;
using WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.PhoneSignIn;

/// <summary>
/// Sign-in by phone over PostgreSQL with a fake bot: the start link, the shared contact and its checks, the code in the
/// linked chat, find-or-create while sign-up is on, the allowed countries and the per-number and per-address limits, and
/// codes kept as digests that a second instance verifies.
/// </summary>
public sealed class PhoneSignInTests
{
    private const string Phone = "+998901234567";
    private const string Address = "203.0.113.5";
    private const long ChatId = 4242;
    private const string HashKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    [Fact]
    public async Task UnlinkedNumber_ShouldLinkThroughTheBot_AndCreateThePersonOnTheFirstSignIn()
    {
        var bot = new FakeTelegramBotClient();
        await using var host = CreateHost(bot);
        await using var scope = host.Scope();
        var services = scope.ServiceProvider;
        var phoneSignIn = services.GetRequiredService<UserPhoneSignInService>();
        var updates = services.GetRequiredService<TelegramUpdateHandler>();

        var sent = await phoneSignIn.SendAsync("+998 90 123 45 67", Address);
        sent.Status.Should().Be(PhoneSignInStatus.LinkRequired);
        sent.TelegramLinkUrl.Should().StartWith("https://t.me/Ocharo_Bot?start=");
        var token = sent.TelegramLinkUrl![(sent.TelegramLinkUrl.IndexOf('=') + 1)..];
        token.Should().MatchRegex("^[A-Za-z0-9_-]{32}$");

        await updates.HandleAsync(Start(token));
        var prompt = bot.To(ChatId).Single();
        prompt.Text.Should().StartWith("Telefon raqamingizni yuborish", "the person's Telegram language is uz");
        prompt.ReplyMarkup.Should().BeOfType<ReplyKeyboardMarkup>().Which.Keyboard.Single().Single().RequestContact.Should().BeTrue();

        await updates.HandleAsync(Contact(Phone, contactUserId: 99));
        bot.To(ChatId).Last().Text.Should().Contain("o'z raqamingizni", "a forwarded card proves nothing");
        await updates.HandleAsync(Contact("+998907654321"));
        bot.To(ChatId).Last().Text.Should().Contain("mos kelmaydi", "the number differs from the one typed");
        (await services.GetRequiredService<ITelegramLinkRepository>().FindLinkAsync("ocharo_bot", Phone)).Should().BeNull();

        await updates.HandleAsync(Contact("998901234567"));
        var codeMessage = bot.To(ChatId).Last();
        codeMessage.ReplyMarkup.Should().BeOfType<ReplyKeyboardRemove>();
        codeMessage.Text.Should().Contain("Ocharo tasdiqlash kodingiz");
        var code = bot.LastCode(ChatId);
        (await services.GetRequiredService<ITelegramLinkRepository>().FindLinkAsync("ocharo_bot", Phone))!.ChatId.Should().Be(ChatId);

        await updates.HandleAsync(Contact(Phone));
        bot.To(ChatId).Last().Text.Should().Contain("Havola eskirgan", "a completed link completes once");

        (await phoneSignIn.VerifyAsync(Phone, Wrong(code), isSignUpAllowed: true)).Status.Should().Be(PhoneSignInStatus.InvalidCode);
        var verified = await phoneSignIn.VerifyAsync(Phone, code, isSignUpAllowed: true);
        verified.Status.Should().Be(PhoneSignInStatus.Verified);
        verified.IsUserCreated.Should().BeTrue();
        verified.User!.Should().BeEquivalentTo(new { UserName = Phone, PhoneNumber = Phone, IsPhoneNumberConfirmed = true }, options => options.ExcludingMissingMembers());
        (await phoneSignIn.VerifyAsync(Phone, code, isSignUpAllowed: true)).Status.Should().Be(PhoneSignInStatus.InvalidCode, "the code is spent");

        var signedIn = await services.GetRequiredService<SignInService>().SignInAsync(verified.User!, [AuthenticationMethodConstants.OneTimePassword], "phone_code");
        signedIn.Status.Should().Be(SignInStatus.Succeeded);
        signedIn.Principal!.FindAll(AuthenticationMethodConstants.ClaimType).Select(c => c.Value).Should().Equal("otp");
    }

    [Fact]
    public async Task LinkedNumber_ShouldGetItsCodeAtOnce_AndSignInTheKnownPerson()
    {
        var bot = new FakeTelegramBotClient();
        await using var host = CreateHost(bot);
        var person = await host.CreateUserAsync("lola");
        await using var scope = host.Scope();
        var services = scope.ServiceProvider;
        var accounts = services.GetRequiredService<UserAccountService>();
        var tracked = (await accounts.FindByIdAsync(person.Id))!;
        tracked.PhoneNumber = Phone;
        tracked.IsPhoneNumberConfirmed = true;
        await accounts.UpdateAsync(tracked);
        await LinkAsync(services, bot);
        host.Time.Advance(TimeSpan.FromSeconds(31));

        var sent = await services.GetRequiredService<UserPhoneSignInService>().SendAsync(Phone, Address);

        sent.Status.Should().Be(PhoneSignInStatus.CodeSent);
        bot.To(ChatId).Last().ReplyMarkup.Should().BeNull();
        var verified = await services.GetRequiredService<UserPhoneSignInService>().VerifyAsync(Phone, bot.LastCode(ChatId), isSignUpAllowed: false);
        verified.Status.Should().Be(PhoneSignInStatus.Verified);
        verified.IsUserCreated.Should().BeFalse();
        verified.User!.Id.Should().Be(person.Id);
    }

    [Fact]
    public async Task FirstSignIn_ShouldBeRefused_WhileSignUpIsOff()
    {
        var bot = new FakeTelegramBotClient();
        await using var host = CreateHost(bot);
        await using var scope = host.Scope();
        var services = scope.ServiceProvider;
        await LinkAsync(services, bot);

        var verified = await services.GetRequiredService<UserPhoneSignInService>().VerifyAsync(Phone, bot.LastCode(ChatId), isSignUpAllowed: false);

        verified.Status.Should().Be(PhoneSignInStatus.SignUpClosed);
        (await services.GetRequiredService<IUserRepository>().FindByConfirmedPhoneNumberAsync(Phone)).Should().BeNull();
    }

    [Fact]
    public async Task BlockedChat_ShouldFallBackToAStartLink()
    {
        var bot = new FakeTelegramBotClient();
        await using var host = CreateHost(bot);
        await using var scope = host.Scope();
        var services = scope.ServiceProvider;
        await LinkAsync(services, bot);
        bot.BlockedChats[ChatId] = true;
        host.Time.Advance(TimeSpan.FromSeconds(31));

        var sent = await services.GetRequiredService<UserPhoneSignInService>().SendAsync(Phone, Address);

        sent.Status.Should().Be(PhoneSignInStatus.LinkRequired);
        sent.TelegramLinkUrl.Should().StartWith("https://t.me/Ocharo_Bot?start=");
        var now = host.Time.GetUtcNow();
        (await services.GetRequiredService<IdentityDbContext>().OtpCodes.AsNoTracking().CountAsync(c => c.ConsumedAt == null && c.ExpiresAt > now))
            .Should().Be(0, "the undelivered code was discarded, and the code before it retired");
    }

    [Fact]
    public async Task LockedOutPerson_ShouldBeRefusedBeforeTheCodeIsSpent()
    {
        var bot = new FakeTelegramBotClient();
        await using var host = CreateHost(bot, identity => identity.AddLockout());
        var person = await host.CreateUserAsync("mira");
        await using var scope = host.Scope();
        var services = scope.ServiceProvider;
        var accounts = services.GetRequiredService<UserAccountService>();
        var tracked = (await accounts.FindByIdAsync(person.Id))!;
        tracked.PhoneNumber = Phone;
        tracked.IsPhoneNumberConfirmed = true;
        await accounts.UpdateAsync(tracked);
        await LinkAsync(services, bot);
        var code = bot.LastCode(ChatId);
        await services.GetRequiredService<UserLockoutService>().LockUntilAsync(tracked, host.Time.GetUtcNow().AddMinutes(2));
        var phoneSignIn = services.GetRequiredService<UserPhoneSignInService>();

        (await phoneSignIn.VerifyAsync(Phone, code, isSignUpAllowed: false)).Status.Should().Be(PhoneSignInStatus.LockedOut);

        host.Time.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));
        (await phoneSignIn.VerifyAsync(Phone, code, isSignUpAllowed: false)).Status.Should().Be(PhoneSignInStatus.Verified, "the refused attempt left the code unspent");
    }

    [Fact]
    public async Task Send_ShouldRefuseOtherCountries_AndLimitPerNumberAndAddress()
    {
        var bot = new FakeTelegramBotClient();
        await using var host = CreateHost(bot);
        await using var scope = host.Scope();
        var phoneSignIn = scope.ServiceProvider.GetRequiredService<UserPhoneSignInService>();

        (await phoneSignIn.SendAsync("+77011234567", Address)).Status.Should().Be(PhoneSignInStatus.CountryNotAllowed);
        (await phoneSignIn.SendAsync("901234567", Address)).Status.Should().Be(PhoneSignInStatus.InvalidNumber);

        (await phoneSignIn.SendAsync(Phone, Address)).Status.Should().Be(PhoneSignInStatus.LinkRequired);
        (await phoneSignIn.SendAsync(Phone, "198.51.100.7")).Status.Should().Be(PhoneSignInStatus.RateLimited, "the number asked moments ago");
        (await phoneSignIn.SendAsync("+998907654321", Address)).Status.Should().Be(PhoneSignInStatus.RateLimited, "the address asked moments ago");

        host.Time.Advance(TimeSpan.FromSeconds(6));
        (await phoneSignIn.SendAsync("+998907654321", Address)).Status.Should().Be(PhoneSignInStatus.LinkRequired);
    }

    [Fact]
    public async Task Codes_ShouldBeDigestsAtRest_AndVerifyOnAnotherInstance_WithinOneAttemptLimit()
    {
        await using var database = new IdentityTestDatabase();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));
        await using var first = CreateInstance(database, time);
        await using var second = CreateInstance(database, time);
        await IdentityTestDatabase.MigrateAsync(first);

        string code;
        await using (var scope = first.CreateAsyncScope())
            code = (await scope.ServiceProvider.GetRequiredService<IOtpService>().CreateAsync(Phone, UserPhoneSignInService.CodeScope)).Code!;

        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            var stored = (await connection.QueryAsync<string>(
                "select concat_ws('|', subject_hash, scope, code_hash, coalesce(origin_hash, '')) from identity.identity_otp_codes")).Single();
            stored.Should().NotContain(code).And.NotContain("998901234567");
        }

        await using (var scope = second.CreateAsyncScope())
        {
            var otp = scope.ServiceProvider.GetRequiredService<IOtpService>();
            for (var attempt = 0; attempt < 4; attempt++)
                (await otp.VerifyAsync(Phone, Wrong(code), UserPhoneSignInService.CodeScope)).FailureReason.Should().Be(OtpFailureReason.InvalidCode);
        }

        await using (var scope = first.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<IOtpService>().VerifyAsync(Phone, code, UserPhoneSignInService.CodeScope)).IsSuccess.Should().BeTrue("the fifth attempt is the last one allowed");

        await using (var scope = second.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<IOtpService>().VerifyAsync(Phone, code, UserPhoneSignInService.CodeScope)).IsSuccess.Should().BeFalse("the other instance spent it");
            var row = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().OtpCodes.SingleAsync();
            row.Attempts.Should().Be(5);
            row.ConsumedAt.Should().NotBeNull();
        }
    }

    /// <summary>Links <see cref="Phone"/> to <see cref="ChatId"/> through the bot, as a person would; the bot's first code is the last message.</summary>
    private static async Task LinkAsync(IServiceProvider services, FakeTelegramBotClient bot)
    {
        var url = (await services.GetRequiredService<UserPhoneSignInService>().SendAsync(Phone, Address)).TelegramLinkUrl!;
        var updates = services.GetRequiredService<TelegramUpdateHandler>();
        await updates.HandleAsync(Start(url[(url.IndexOf('=') + 1)..]));
        await updates.HandleAsync(Contact(Phone));
        bot.LastCode(ChatId).Should().HaveLength(6);
    }

    private static AccountsHost CreateHost(FakeTelegramBotClient bot, Action<IdentityBuilder>? slices = null) => AccountsHost.Create(
        identity => (slices is null ? identity : Apply(identity, slices))
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

    private static IdentityBuilder Apply(IdentityBuilder identity, Action<IdentityBuilder> slices)
    {
        slices(identity);
        return identity;
    }

    private static ServiceProvider CreateInstance(IdentityTestDatabase database, TimeProvider time)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(time);
        database.Register(services);
        services.AddUserAccounts().AddDatabaseOtpStore();
        services.AddOtpService(o => o.HashKey = HashKey);
        return services.BuildServiceProvider();
    }

    private static Update Start(string token) => Update(message => message.Text = "/start " + token);

    private static Update Contact(string phone, long contactUserId = ChatId)
        => Update(message => message.Contact = new Contact { PhoneNumber = phone, FirstName = "Lola", UserId = contactUserId });

    private static Update Update(Action<Message> shape)
    {
        var message = new Message
        {
            Id = 1,
            Date = DateTime.UtcNow,
            Chat = new Chat { Id = ChatId, Type = ChatType.Private },
            From = new User { Id = ChatId, IsBot = false, FirstName = "Lola", LanguageCode = "uz" },
        };
        shape(message);
        return new Update { Id = 1, Message = message };
    }

    private static string Wrong(string code) => code == "000000" ? "111111" : "000000";
}

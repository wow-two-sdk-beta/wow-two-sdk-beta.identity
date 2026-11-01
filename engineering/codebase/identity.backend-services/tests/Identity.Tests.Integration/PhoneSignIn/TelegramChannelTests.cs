using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WoW.Two.Sdk.Beta.Be.Comms.Email;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Emails;
using WoW.Two.Sdk.Beta.Be.Identity.Core.OneTimeCodes;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;
using WoW.Two.Sdk.Beta.Be.Identity.Otp.Email;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;
using WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.PhoneSignIn;

/// <summary>
/// A linked Telegram chat as a verified channel, over PostgreSQL: a password-change code or link reaches a person who has only
/// a confirmed phone, email stays first with Telegram as the fallback, and an unlinked or unconfirmed number reaches nothing.
/// </summary>
public sealed class TelegramChannelTests
{
    private const string Phone = "+998901234567";
    private const string Email = "lola@example.test";
    private const string Password = "correct horse battery";
    private const string Replacement = "a brand new passphrase";
    private const string HashKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const long ChatId = 4242;

    [Fact]
    public async Task PasswordChange_ShouldSendTheCodeToTelegram_ForAPersonWithOnlyAConfirmedPhone()
    {
        var bot = new FakeTelegramBotClient();
        await using var host = CreateHost(bot, PasswordChangeConfirmationType.Code, new SwitchableBroker());
        await using var scope = host.Scope();
        var services = scope.ServiceProvider;
        var person = await CreatePersonAsync(services, withEmail: false);
        await LinkAsync(services, bot);
        var change = services.GetRequiredService<UserPasswordChangeService>();

        var sent = await change.SendConfirmationAsync(person);

        sent.Status.Should().Be(PasswordChangeConfirmationStatus.Sent);
        bot.To(ChatId).Last().Text.Should().Contain("Ocharo", "the code is worded with the app's name");
        var code = bot.LastCode(ChatId);
        (await change.ChangeAsync(person, Password, Replacement, code: "000000", isTicketed: false)).IsSuccess.Should().BeFalse("a wrong code changes nothing");
        (await change.ChangeAsync(person, Password, Replacement, code, isTicketed: false)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task PasswordChange_ShouldSendTheLinkToTelegram_UnderLink()
    {
        var bot = new FakeTelegramBotClient();
        await using var host = CreateHost(bot, PasswordChangeConfirmationType.Link, new SwitchableBroker(), linkTemplate: "https://id.example.test/change-password#token={token}");
        await using var scope = host.Scope();
        var services = scope.ServiceProvider;
        var person = await CreatePersonAsync(services, withEmail: false);
        await LinkAsync(services, bot);

        var sent = await services.GetRequiredService<UserPasswordChangeService>().SendConfirmationAsync(person);

        sent.Status.Should().Be(PasswordChangeConfirmationStatus.Sent);
        bot.To(ChatId).Last().Text.Should().Contain("https://id.example.test/change-password#token=" + person.Id.ToString("N") + ".");
    }

    [Fact]
    public async Task PasswordChange_ShouldPreferEmail_AndFallBackToTelegram_WhenTheEmailFails()
    {
        var bot = new FakeTelegramBotClient();
        var broker = new SwitchableBroker();
        await using var host = CreateHost(bot, PasswordChangeConfirmationType.Code, broker);
        await using var scope = host.Scope();
        var services = scope.ServiceProvider;
        var person = await CreatePersonAsync(services, withEmail: true);
        await LinkAsync(services, bot);
        var sentBefore = bot.Sent.Count;
        var change = services.GetRequiredService<UserPasswordChangeService>();

        (await change.SendConfirmationAsync(person)).Status.Should().Be(PasswordChangeConfirmationStatus.Sent);
        broker.Sent.Should().ContainSingle("email is listed first");
        bot.Sent.Count.Should().Be(sentBefore, "Telegram was not needed");

        host.Time.Advance(TimeSpan.FromMinutes(1));
        broker.IsFailing = true;
        (await change.SendConfirmationAsync(person)).Status.Should().Be(PasswordChangeConfirmationStatus.Sent);
        bot.Sent.Count.Should().Be(sentBefore + 1, "the failed email falls through to the linked chat");
        bot.To(ChatId).Last().Text.Should().MatchRegex("[0-9]{6}");
    }

    [Fact]
    public async Task PasswordChange_ShouldReachNothing_ForAnUnlinkedOrUnconfirmedNumber()
    {
        var bot = new FakeTelegramBotClient();
        await using var host = CreateHost(bot, PasswordChangeConfirmationType.Code, new SwitchableBroker());
        await using var scope = host.Scope();
        var services = scope.ServiceProvider;
        var person = await CreatePersonAsync(services, withEmail: false);
        var change = services.GetRequiredService<UserPasswordChangeService>();

        (await change.SendConfirmationAsync(person)).Status.Should().Be(PasswordChangeConfirmationStatus.NoVerifiedChannel, "no chat is linked yet");

        await LinkAsync(services, bot);
        person.IsPhoneNumberConfirmed = false;
        (await change.SendConfirmationAsync(person)).Status.Should().Be(PasswordChangeConfirmationStatus.NoVerifiedChannel, "an unconfirmed number proves nothing");
    }

    private static AccountsHost CreateHost(FakeTelegramBotClient bot, PasswordChangeConfirmationType confirmation, SwitchableBroker broker, string? linkTemplate = null) => AccountsHost.Create(
        identity => identity
            .AddArgon2Passwords()
            .AddUserTokens(o => o.SigningKey = AccountsHost.SigningKey)
            .AddEmailConfirmation()
            .AddPasswordChange()
            .AddSignIn()
            .AddDatabaseOtpStore()
            .AddPhoneSignIn(Telegram, phones => phones.AllowedCountries = ["UZ"]),
        options =>
        {
            options.Password.Change.Confirmation = confirmation;
            options.Password.Change.LinkTemplate = linkTemplate;
        },
        services =>
        {
            services.AddOtpService(o =>
            {
                o.HashKey = HashKey;
                o.Messages.AppName = "Ocharo";
                o.RateLimitWindow = TimeSpan.FromSeconds(30);
            });
            services.AddEmailOtpDelivery();
            services.RemoveAll<IEmailBroker>();
            services.AddSingleton<IEmailBroker>(broker);
            services.RemoveAll<ITelegramBotClient>();
            services.AddSingleton<ITelegramBotClient>(bot);
        });

    private static void Telegram(TelegramBotOptions bot)
    {
        bot.IsEnabled = true;
        bot.BotToken = "123456:TEST-token";
        bot.BotUserName = "Ocharo_Bot";
        bot.WebhookSecret = "hook-secret";
    }

    /// <summary>Creates a person holding <see cref="Phone"/> confirmed, with a password and, if asked, a confirmed email.</summary>
    private static async Task<IdentityUser> CreatePersonAsync(IServiceProvider services, bool withEmail)
    {
        var person = new IdentityUser
        {
            UserName = "lola",
            PhoneNumber = Phone,
            IsPhoneNumberConfirmed = true,
            Email = withEmail ? Email : null,
            IsEmailConfirmed = withEmail,
        };
        (await services.GetRequiredService<UserPasswordService>().CreateWithPasswordAsync(person, Password)).IsSuccess.Should().BeTrue();
        return person;
    }

    /// <summary>Links <see cref="Phone"/> to <see cref="ChatId"/> through the bot, as a person would.</summary>
    private static async Task LinkAsync(IServiceProvider services, FakeTelegramBotClient bot)
    {
        var url = (await services.GetRequiredService<UserPhoneSignInService>().SendAsync(Phone, "203.0.113.5")).TelegramLinkUrl!;
        var updates = services.GetRequiredService<TelegramUpdateHandler>();
        await updates.HandleAsync(Update(message => message.Text = "/start " + url[(url.IndexOf('=') + 1)..]));
        await updates.HandleAsync(Update(message => message.Contact = new Contact { PhoneNumber = Phone, FirstName = "Lola", UserId = ChatId }));
        bot.LastCode(ChatId).Should().HaveLength(6);
    }

    private static Update Update(Action<Message> shape)
    {
        var message = new Message
        {
            Id = 1,
            Date = DateTime.UtcNow,
            Chat = new Chat { Id = ChatId, Type = ChatType.Private },
            From = new User { Id = ChatId, IsBot = false, FirstName = "Lola", LanguageCode = "en" },
        };
        shape(message);
        return new Update { Id = 1, Message = message };
    }

    /// <summary>An email broker that keeps what it accepts and can be told to fail.</summary>
    private sealed class SwitchableBroker : IEmailBroker
    {
        private readonly List<EmailMessage> _sent = [];

        public bool IsFailing { get; set; }

        public IReadOnlyList<EmailMessage> Sent => _sent;

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (IsFailing)
                return Task.FromResult(new EmailSendResult { Success = false, FailureReason = "relay_down" });

            _sent.Add(message);
            return Task.FromResult(new EmailSendResult { Success = true });
        }
    }
}

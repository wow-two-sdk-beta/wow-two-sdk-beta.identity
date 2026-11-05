using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;
using WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;
using WoW.Two.Sdk.Beta.Be.Identity.Jwt.Issuance;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Identity.Tests.E2E.PhoneSignIn;

/// <summary>
/// A new browser's own-code sign-in waits for a trusted contact's press, through the real service with a fake bot. Asked
/// by the developer on 2026-11-05 as "any new browser or device", which also catches a recycled SIM logged in to the old
/// Telegram account: the code alone no longer signs in, and the browser is remembered once a contact approves.
/// </summary>
public sealed class NewDeviceConfirmationApiTests
{
    private const string Secret = "hook-secret_e2e";
    private const string BotUserName = "ocharo_e2e_bot";
    private const string AccountPhone = "+998901234567";
    private const long AccountChat = 9303;
    private const string ContactPhone = "+998907777777";
    private const long ContactChat = 9101;
    private const string SecondContactPhone = "+998905555555";
    private const long SecondContactChat = 9202;
    private const string Browser = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

    [Fact]
    public async Task NewBrowser_ShouldWaitForAPress_ThenSignIn_AndKnowTheBrowserAfter()
    {
        await WithHostAsync(async (host, bot) =>
        {
            var (account, contact, second) = await SeedAsync(host);
            using var browser = Client(host);

            var waiting = await OwnCodeAsync(host, bot, browser);

            // No session, and the code alone isn't enough.
            waiting.GetProperty("contacts").EnumerateArray().Select(c => c.GetString()).Should().Equal("C*** · +998*******77", "S*** · +998*******55");
            (await browser.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            await StatusAsync(browser, waiting, "pending");

            // Each contact hears who, on what and from where, with two buttons and no code.
            foreach (var chat in new[] { ContactChat, SecondContactChat })
            {
                var notice = bot.To(chat).Last();
                notice.Text.Should().Contain("Maftuna").And.Contain("Ocharo").And.Contain("Chrome on macOS");
                bot.To(chat).Should().ContainSingle("a contact gets a question here, never a code");
                ((InlineKeyboardMarkup)notice.ReplyMarkup!).InlineKeyboard.Select(row => row.Single().Text).Should().Equal("Approve", "Deny");
            }

            // Only that contact's own Telegram user counts.
            (await PressAsync(browser, ContactChat, bot, "Approve", SecondContactChat)).StatusCode.Should().Be(HttpStatusCode.OK);
            bot.Answers.Last().Text.Should().Be("This button is not for you.");
            await StatusAsync(browser, waiting, "pending");

            await PressAsync(browser, ContactChat, bot, "Approve");
            bot.TextEdits.Last().Text.Should().Contain("Approved");

            (await PollAsync(browser, waiting)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await browser.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await DataAsync(await browser.GetAsync("/api/account/manage/trusted-contacts"))).GetProperty("canRevoke").GetBoolean()
                .Should().BeTrue("the session came from the account's own code");
            (await CodeOfAsync(await PollAsync(Client(host), waiting))).Should().Be("DeviceConfirmationExpired", "a confirmation signs in once");

            // The other contact's press now changes nothing.
            await PressAsync(browser, SecondContactChat, bot, "Deny");
            bot.TextEdits.Last().Text.Should().Contain("expired");
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var known = await context.KnownDevices.SingleAsync(d => d.UserId == account);
                known.ConfirmedByUserId.Should().Be(contact);
                _ = second;
            }

            // The known browser signs straight in, telling no contact.
            var noticesBefore = bot.To(ContactChat).Count;
            (await browser.PostAsync("/api/account/sign-out", null)).Dispose();

            using var again = await OwnCodeAnswerAsync(host, bot, browser);
            again.StatusCode.Should().Be(HttpStatusCode.NoContent, await again.Content.ReadAsStringAsync());
            bot.To(ContactChat).Count.Should().Be(noticesBefore);

            // Another browser is new.
            using var other = Client(host);
            await OwnCodeAsync(host, bot, other);
            bot.To(ContactChat).Count.Should().Be(noticesBefore + 1);
        });
    }

    [Fact]
    public async Task Deny_ShouldStopTheSignIn_LogIt_AndLeaveTheBrowserNew()
    {
        await WithHostAsync(async (host, bot) =>
        {
            var (account, _, _) = await SeedAsync(host);
            using var browser = Client(host);
            var waiting = await OwnCodeAsync(host, bot, browser);

            await PressAsync(browser, ContactChat, bot, "Deny");
            bot.TextEdits.Last().Text.Should().Contain("Denied");

            (await CodeOfAsync(await PollAsync(browser, waiting))).Should().Be("DeviceConfirmationDenied");
            (await browser.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                (await context.SignInLog.AnyAsync(e => e.UserId == account && e.Reason == "device_denied")).Should().BeTrue();
                (await context.KnownDevices.AnyAsync()).Should().BeFalse();
            }

            // The browser asks again, and a new request can be approved.
            var next = await OwnCodeAsync(host, bot, browser);
            await PressAsync(browser, SecondContactChat, bot, "Approve");
            (await PollAsync(browser, next)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        });
    }

    [Fact]
    public async Task Expiry_ShouldEndTheWait_AndARevokedContactsPressShouldChangeNothing()
    {
        await WithHostAsync(async (host, bot) =>
        {
            var (account, _, second) = await SeedAsync(host);
            using var browser = Client(host);
            var waiting = await OwnCodeAsync(host, bot, browser);

            // A contact revoked while the request waits no longer answers for the account.
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var services = scope.ServiceProvider;
                var person = (await services.GetRequiredService<UserAccountService>().FindByIdAsync(account))!;
                (await services.GetRequiredService<TrustedContactService>().RevokeAsync(person, second)).Should().BeTrue();
            }

            await PressAsync(browser, SecondContactChat, bot, "Approve");
            bot.TextEdits.Last().Text.Should().Contain("expired");
            await StatusAsync(browser, waiting, "pending");

            host.Clock.Advance(TimeSpan.FromMinutes(10));
            (await CodeOfAsync(await PollAsync(browser, waiting))).Should().Be("DeviceConfirmationExpired");
            await PressAsync(browser, ContactChat, bot, "Approve");
            bot.TextEdits.Last().Text.Should().Contain("expired");
            await using (var scope = host.Services.CreateAsyncScope())
                (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().KnownDevices.AnyAsync()).Should().BeFalse();
        });
    }

    [Fact]
    public async Task NewRequest_ShouldReplaceAnOpenOne_SpendingItsButtons()
    {
        await WithHostAsync(async (host, bot) =>
        {
            await SeedAsync(host);
            using var first = Client(host);
            var firstWaiting = await OwnCodeAsync(host, bot, first);
            var staleButton = ButtonOf(bot, ContactChat, "Approve");

            using var second = Client(host);
            var secondWaiting = await OwnCodeAsync(host, bot, second);

            (await CodeOfAsync(await PollAsync(first, firstWaiting))).Should().Be("DeviceConfirmationExpired");
            await PostUpdateAsync(first, CallbackUpdate(staleButton, ContactChat));
            bot.Answers.Last().Text.Should().Be("This button no longer works.");
            await StatusAsync(second, secondWaiting, "pending");
        });
    }

    [Fact]
    public async Task ThroughAContact_ShouldAskNobody_AndMakeTheBrowserKnown()
    {
        await WithHostAsync(async (host, bot) =>
        {
            var (_, contact, _) = await SeedAsync(host);
            using var browser = Client(host);
            var target = (await DataAsync(await browser.PostAsJsonAsync("/api/account/phone-sign-in/targets", new { phone = AccountPhone })))
                .GetProperty("trustedContacts").EnumerateArray().First().GetProperty("id").GetString();
            (await DataAsync(await browser.PostAsJsonAsync("/api/account/phone-sign-in/send", new { phone = AccountPhone, targetId = target }))).GetProperty("isCodeSent").GetBoolean().Should().BeTrue();

            using var signedIn = await browser.PostAsJsonAsync("/api/account/phone-sign-in?useCookies=true", new { phone = AccountPhone, code = bot.LastCode(ContactChat) });
            signedIn.StatusCode.Should().Be(HttpStatusCode.NoContent, await signedIn.Content.ReadAsStringAsync());

            await using (var scope = host.Services.CreateAsyncScope())
            {
                var known = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().KnownDevices.SingleAsync();
                known.ConfirmedByUserId.Should().Be(contact);
            }

            // The account's own code now passes on that browser, and on no other.
            host.Clock.Advance(TimeSpan.FromSeconds(31));
            using var own = await OwnCodeAnswerAsync(host, bot, browser);
            own.StatusCode.Should().Be(HttpStatusCode.NoContent, await own.Content.ReadAsStringAsync());
            using var other = Client(host);
            await OwnCodeAsync(host, bot, other);
        });
    }

    [Fact]
    public async Task RequestFields_ShouldNotCarryWordsOfTheSigner_IntoTheQuestion()
    {
        await WithHostAsync(async (host, bot) =>
        {
            await SeedAsync(host);
            using var browser = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            browser.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "It is really me please approve");
            (await DataAsync(await browser.PostAsJsonAsync("/api/account/phone-sign-in/send", new { phone = AccountPhone }))).GetProperty("isCodeSent").GetBoolean().Should().BeTrue();

            var answer = await DataAsync(await browser.PostAsJsonAsync("/api/account/phone-sign-in?useCookies=true&clientId=Approve%20this%20now", new { phone = AccountPhone, code = bot.LastCode(AccountChat) }));

            answer.TryGetProperty("deviceConfirmation", out _).Should().BeTrue();
            bot.To(ContactChat).Last().Text.Should().NotContain("really me").And.NotContain("Approve this").And.Contain("unknown").And.Contain("Ocharo");
        });
    }

    [Fact]
    public async Task AccountDeletion_ShouldRemoveItsDevicesAndRequests()
    {
        await WithHostAsync(async (host, bot) =>
        {
            var (account, _, _) = await SeedAsync(host);
            using var known = Client(host);
            var waiting = await OwnCodeAsync(host, bot, known);
            await PressAsync(known, ContactChat, bot, "Approve");
            (await PollAsync(known, waiting)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            using var open = Client(host);
            await OwnCodeAsync(host, bot, open);

            await using var scope = host.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var context = services.GetRequiredService<IdentityDbContext>();
            (await context.KnownDevices.CountAsync(d => d.UserId == account)).Should().Be(1);
            (await context.DeviceConfirmations.CountAsync(c => c.UserId == account)).Should().Be(1);

            var person = (await services.GetRequiredService<UserAccountService>().FindByIdAsync(account))!;
            await services.GetServices<IAccountDeletionHandler>().OfType<TrustedContactDeletionHandler>().Single().HandleAsync(person);

            (await context.KnownDevices.AnyAsync(d => d.UserId == account)).Should().BeFalse();
            (await context.DeviceConfirmations.AnyAsync(c => c.UserId == account)).Should().BeFalse();
        });
    }

    [Fact]
    public async Task NobodyToAsk_ShouldSignStraightIn()
    {
        await WithHostAsync(async (host, bot) =>
        {
            var account = await CreatePersonAsync(host, "Maftuna", AccountPhone, AccountChat);
            var unlinked = await CreatePersonAsync(host, "Unlinked", ContactPhone, chatId: null);
            await RecordAsync(host, account, unlinked);

            using var browser = Client(host);
            using var signedIn = await OwnCodeAnswerAsync(host, bot, browser);

            signedIn.StatusCode.Should().Be(HttpStatusCode.NoContent, await signedIn.Content.ReadAsStringAsync());
            await using var scope = host.Services.CreateAsyncScope();
            (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().DeviceConfirmations.AnyAsync()).Should().BeFalse();
        });
    }

    [Fact]
    public async Task EveryChatRefusing_ShouldAnswerConflict_AndStartNoSession()
    {
        await WithHostAsync(async (host, bot) =>
        {
            await SeedAsync(host);
            bot.BlockedChats[ContactChat] = true;
            bot.BlockedChats[SecondContactChat] = true;
            using var browser = Client(host);

            using var refused = await OwnCodeAnswerAsync(host, bot, browser);

            (await CodeOfAsync(refused)).Should().Be("DeviceConfirmationUnavailable");
            (await browser.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        });
    }

    [Fact]
    public async Task SettingOff_ShouldSignStraightIn()
    {
        await WithHostAsync(
            async (host, bot) =>
            {
                await SeedAsync(host);
                using var browser = Client(host);
                using var signedIn = await OwnCodeAnswerAsync(host, bot, browser);
                signedIn.StatusCode.Should().Be(HttpStatusCode.NoContent, await signedIn.Content.ReadAsStringAsync());
            },
            isRequired: false);
    }

    [Fact]
    public async Task ChoosingOnesOwnAccount_ShouldWaitToo()
    {
        await WithHostAsync(async (host, bot) =>
        {
            // The account is itself someone's trusted contact, so its number offers a choice first.
            var (account, _, _) = await SeedAsync(host);
            var trusting = await CreatePersonAsync(host, "Lola", "+998903333333", 9404);
            await RecordAsync(host, trusting, account);
            using var browser = Client(host);
            (await DataAsync(await browser.PostAsJsonAsync("/api/account/phone-sign-in/send", new { phone = AccountPhone }))).GetProperty("isCodeSent").GetBoolean().Should().BeTrue();
            var choice = (await DataAsync(await browser.PostAsJsonAsync("/api/account/phone-sign-in?useCookies=true", new { phone = AccountPhone, code = bot.LastCode(AccountChat) }))).GetProperty("accountChoice");

            var answer = await DataAsync(await browser.PostAsJsonAsync("/api/account/phone-sign-in/choose?useCookies=true", new { token = choice.GetProperty("token").GetString(), userId = account }));

            var waiting = answer.GetProperty("deviceConfirmation");
            (await browser.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            await PressAsync(browser, ContactChat, bot, "Approve");
            (await PollAsync(browser, waiting)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        });
    }

    [Fact]
    public async Task Bearer_ShouldAskEveryTime_SinceItKeepsNoCookie()
    {
        await WithHostAsync(async (host, bot) =>
        {
            await SeedAsync(host);
            using var browser = Client(host);
            (await DataAsync(await browser.PostAsJsonAsync("/api/account/phone-sign-in/send", new { phone = AccountPhone }))).GetProperty("isCodeSent").GetBoolean().Should().BeTrue();

            var answer = await browser.PostAsJsonAsync("/api/account/phone-sign-in", new { phone = AccountPhone, code = bot.LastCode(AccountChat) });
            answer.Headers.Contains("Set-Cookie").Should().BeFalse("a bearer client keeps no cookie, and gets none");
            var waiting = (await DataAsync(answer)).GetProperty("deviceConfirmation");
            await PressAsync(browser, ContactChat, bot, "Approve");

            var polled = await browser.PostAsJsonAsync("/api/account/phone-sign-in/confirmation", new { token = waiting.GetProperty("token").GetString() });
            (await DataAsync(polled)).GetProperty("accessToken").GetString().Should().Be(FixedTokenIssuer.Token);

            await using (var scope = host.Services.CreateAsyncScope())
                (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().KnownDevices.AnyAsync()).Should().BeFalse("nothing can name a bearer client later");
            host.Clock.Advance(TimeSpan.FromSeconds(31));
            (await DataAsync(await browser.PostAsJsonAsync("/api/account/phone-sign-in/send", new { phone = AccountPhone }))).GetProperty("isCodeSent").GetBoolean().Should().BeTrue();
            (await DataAsync(await browser.PostAsJsonAsync("/api/account/phone-sign-in", new { phone = AccountPhone, code = bot.LastCode(AccountChat) }))).TryGetProperty("deviceConfirmation", out _).Should().BeTrue();
        },
        hasBearer: true);
    }

    [Fact]
    public async Task InvitationThroughTheBot_ShouldNotSignAKnownPersonInOnANewBrowser()
    {
        await WithHostAsync(async (host, bot) =>
        {
            var (account, contact, _) = await SeedAsync(host);
            string token;
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var services = scope.ServiceProvider;
                var organizations = services.GetRequiredService<OrganizationService>();
                var owner = (await services.GetRequiredService<UserAccountService>().FindByIdAsync(contact))!;
                var organization = new IdentityOrganization { Name = "Ocharo", Slug = "ocharo" };
                (await organizations.CreateAsync(organization, owner)).IsSuccess.Should().BeTrue();
                token = (await services.GetRequiredService<InvitationService>().CreateAsync(organization, owner, "editor", phoneNumber: null, email: null)).Token!;
            }

            using var browser = Client(host);
            using var form = new MultipartFormDataContent { { new StringContent("Maftuna"), "displayName" } };
            var link = (await DataAsync(await browser.PostAsync($"/api/account/invitations/{token}/sign-up", form))).GetProperty("telegramLinkUrl").GetString()!;
            await ShareAsync(browser, link, AccountChat, AccountPhone);

            var status = await DataAsync(await browser.GetAsync("/api/account/invitations/sign-up/status"));

            status.GetProperty("status").GetString().Should().Be("completed");
            status.GetProperty("isSignedIn").GetBoolean().Should().BeFalse("a bot share doesn't stand in for a contact's press");
            (await browser.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            await using (var scope = host.Services.CreateAsyncScope())
                (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Memberships.AnyAsync(m => m.UserId == account)).Should().BeTrue("the person still joined");
        });
    }

    private static async Task WithHostAsync(Func<WebApiTestHost<Program>, FakeTelegramBotClient, Task> test, bool isRequired = true, bool hasBearer = false)
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync();
        try
        {
            var bot = new FakeTelegramBotClient();
            await using var host = CreateHost(connectionString, bot, isRequired, hasBearer);
            await test(host, bot);
        }
        finally
        {
            await PostgresTestServer.DropDatabaseAsync(connectionString);
        }
    }

    private static WebApiTestHost<Program> CreateHost(string connectionString, FakeTelegramBotClient bot, bool isRequired, bool hasBearer)
    {
        var host = new WebApiTestHost<Program>
        {
            ConfigureConfigurationHook = configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseSettings:ConnectionString"] = connectionString,
                ["AllowedHosts"] = "localhost",
                ["Logging:LogLevel:Default"] = "Warning",
                ["Identity:Server:Issuer"] = "https://localhost/",
                ["Identity:SignUp:Mode"] = "Invite",
                ["Identity:SignUp:Profile:DisplayName"] = "Required",
                ["Identity:Methods:Password:IsEnabled"] = "false",
                ["Identity:Methods:Email:IsEnabled"] = "false",
                ["Identity:Telegram:IsEnabled"] = "true",
                ["Identity:Telegram:BotToken"] = "123456:E2E-token",
                ["Identity:Telegram:BotUserName"] = BotUserName,
                ["Identity:Telegram:WebhookSecret"] = Secret,
                ["Identity:Phone:AllowedCountries:0"] = "UZ",
                ["Identity:Otp:HashKey"] = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
                ["Identity:Otp:OriginRateLimitWindow"] = "00:00:00",
                ["Identity:Otp:Messages:AppName"] = "Ocharo",
                ["Identity:TrustedContacts:IsEnabled"] = "true",
                ["Identity:TrustedContacts:IsNewDeviceConfirmationRequired"] = isRequired ? "true" : "false",
            }),
            ConfigureServicesHook = services =>
            {
                services.RemoveAll<ITelegramBotClient>();
                services.AddSingleton<ITelegramBotClient>(bot);
                if (hasBearer)
                    services.AddSingleton<ITokenIssuer, FixedTokenIssuer>();
            },
        };
        host.Clock.SetUtcNow(DateTimeOffset.UtcNow);
        return host;
    }

    /// <summary>An account with its own phone and two deliverable contacts.</summary>
    private static async Task<(Guid Account, Guid Contact, Guid Second)> SeedAsync(WebApiTestHost<Program> host)
    {
        var account = await CreatePersonAsync(host, "Maftuna", AccountPhone, AccountChat);
        var contact = await CreatePersonAsync(host, "Contact", ContactPhone, ContactChat);
        var second = await CreatePersonAsync(host, "Second", SecondContactPhone, SecondContactChat);
        await RecordAsync(host, account, contact);
        await RecordAsync(host, account, second);
        return (account, contact, second);
    }

    private static async Task<Guid> CreatePersonAsync(WebApiTestHost<Program> host, string name, string phone, long? chatId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var person = new IdentityUser { UserName = phone, DisplayName = name, PhoneNumber = phone, IsPhoneNumberConfirmed = true };
        (await scope.ServiceProvider.GetRequiredService<UserAccountService>().CreateAsync(person)).IsSuccess.Should().BeTrue();
        if (chatId is { } chat)
        {
            await scope.ServiceProvider.GetRequiredService<ITelegramLinkRepository>().SaveLinkAsync(new IdentityTelegramLink
            {
                Id = Guid.CreateVersion7(),
                BotUserName = BotUserName,
                PhoneNumber = phone,
                ChatId = chat,
                TelegramUserId = chat,
                LinkedAt = DateTimeOffset.UtcNow,
            });
        }

        return person.Id;
    }

    private static async Task RecordAsync(WebApiTestHost<Program> host, Guid userId, Guid contactUserId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<TrustedContactService>().RecordAsync(userId, contactUserId, contactUserId)).IsSuccess.Should().BeTrue();
    }

    /// <summary>Asks for and enters the account's own code, leaving the 30 s window behind.</summary>
    private static async Task<HttpResponseMessage> OwnCodeAnswerAsync(WebApiTestHost<Program> host, FakeTelegramBotClient bot, HttpClient client)
    {
        (await DataAsync(await client.PostAsJsonAsync("/api/account/phone-sign-in/send", new { phone = AccountPhone }))).GetProperty("isCodeSent").GetBoolean().Should().BeTrue();
        var answer = await client.PostAsJsonAsync("/api/account/phone-sign-in?useCookies=true", new { phone = AccountPhone, code = bot.LastCode(AccountChat) });
        host.Clock.Advance(TimeSpan.FromSeconds(31));
        return answer;
    }

    private static async Task<JsonElement> OwnCodeAsync(WebApiTestHost<Program> host, FakeTelegramBotClient bot, HttpClient client)
    {
        return (await DataAsync(await OwnCodeAnswerAsync(host, bot, client))).GetProperty("deviceConfirmation");
    }

    private static Task<HttpResponseMessage> PollAsync(HttpClient client, JsonElement waiting)
        => client.PostAsJsonAsync("/api/account/phone-sign-in/confirmation?useCookies=true", new { token = waiting.GetProperty("token").GetString() });

    private static async Task StatusAsync(HttpClient client, JsonElement waiting, string status)
        => (await DataAsync(await PollAsync(client, waiting))).GetProperty("status").GetString().Should().Be(status);

    private static string ButtonOf(FakeTelegramBotClient bot, long chat, string label)
        => ((InlineKeyboardMarkup)bot.To(chat).Last().ReplyMarkup!).InlineKeyboard.Select(row => row.Single()).Single(button => button.Text == label).CallbackData!;

    private static Task<HttpResponseMessage> PressAsync(HttpClient client, long chat, FakeTelegramBotClient bot, string label, long? from = null)
        => PostUpdateAsync(client, CallbackUpdate(ButtonOf(bot, chat, label), from ?? chat));

    private static HttpClient Client(WebApiTestHost<Program> host)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        client.DefaultRequestHeaders.UserAgent.ParseAdd(Browser);
        return client;
    }

    private static async Task ShareAsync(HttpClient client, string link, long chatId, string phone)
    {
        (await PostUpdateAsync(client, MessageUpdate(chatId, $"\"text\": \"/start {link[(link.IndexOf('=') + 1)..]}\""))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostUpdateAsync(client, MessageUpdate(chatId, $"\"contact\": {{ \"phone_number\": \"{phone.TrimStart('+')}\", \"first_name\": \"Maftuna\", \"user_id\": {chatId} }}"))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<HttpResponseMessage> PostUpdateAsync(HttpClient client, string update)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/telegram/webhook") { Content = new StringContent(update, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", Secret);
        return await client.SendAsync(request);
    }

    private static string MessageUpdate(long chatId, string body) => $$"""
        {
          "update_id": 1,
          "message": {
            "message_id": 1,
            "date": 1730000000,
            "chat": { "id": {{chatId}}, "type": "private" },
            "from": { "id": {{chatId}}, "is_bot": false, "first_name": "Maftuna" },
            {{body}}
          }
        }
        """;

    private static string CallbackUpdate(string data, long fromId) => $$"""
        {
          "update_id": 2,
          "callback_query": {
            "id": "press-1",
            "from": { "id": {{fromId}}, "is_bot": false, "first_name": "Contact", "language_code": "en" },
            "chat_instance": "1",
            "data": "{{data}}"
          }
        }
        """;

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.OK, body);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.GetProperty("data").Clone();
        }
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            using var problem = JsonDocument.Parse(body);
            return problem.RootElement.TryGetProperty("identityCode", out var code) ? code.GetString() : body;
        }
    }

    /// <summary>Stands in for the host's token issuer, which this phone-only host doesn't configure.</summary>
    private sealed class FixedTokenIssuer : ITokenIssuer
    {
        public const string Token = "issued-token";

        /// <inheritdoc />
        public string Issue(IEnumerable<Claim> claims, TokenIssuanceContext? context = null) => Token;
    }
}

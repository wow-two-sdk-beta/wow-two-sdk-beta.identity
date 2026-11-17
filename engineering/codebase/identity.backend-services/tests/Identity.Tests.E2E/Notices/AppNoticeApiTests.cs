using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Identity.Tests.E2E.PhoneSignIn;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Notices;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Identity.Tests.E2E.Notices;

/// <summary>
/// App notices through the real service with the real OIDC server and a fake bot: a client granted
/// <c>identity.notices.send</c> reaches a linked chat once per idempotency key; a client without it gets <c>403</c>, an unlinked
/// <c>sub</c> <c>404</c>; a person who pressed the mute button, or sent <c>/stop</c>, gets nothing and the call reads
/// <c>muted</c>, while their sign-in code still arrives; a bare <c>/start</c> turns notices on again; past the app's rate
/// limit the route answers <c>429</c>.
/// </summary>
public sealed class AppNoticeApiTests
{
    private const string Audience = "https://localhost/api/identity/";
    private const string Events = "e2e-events";
    private const string Reader = "e2e-reader";
    private const string Secret = "e2e-test-only-notice-secret";
    private const string HookSecret = "hook-secret_e2e";
    private const string BotUserName = "ocharo_e2e_bot";
    private const string OwnerPhone = "+998907777777";
    private const long OwnerChat = 9101;
    private const string MemberPhone = "+998905555555";
    private const long MemberChat = 9202;

    [Fact]
    public async Task ScopedClient_ShouldReachTheLinkedChatOncePerKey_AndOthersBeRefused()
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync();
        try
        {
            var bot = new FakeTelegramBotClient();
            await using var host = CreateHost(connectionString, bot);
            using var http = Client(host);
            var owner = await CreatePersonAsync(host, OwnerPhone, OwnerChat);
            var unlinked = await CreatePersonAsync(host, "+998901111111", chatId: null);
            var notice = Notice(owner, "<b>Fashion week</b> opens on Monday. <a href=\"https://events.example.test/e/1\">Details</a>", "digest-1:owner");

            (await SendAsync(http, null, notice)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            using (var unscoped = await SendAsync(http, await TokenAsync(http, Reader, "identity.people.read"), notice))
                unscoped.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a client the declaration grants no identity.notices.send");
            bot.To(OwnerChat).Should().BeEmpty();

            var token = await TokenAsync(http, Events, AppNoticeConstants.Scope);
            var first = await DeliveryAsync(await SendAsync(http, token, notice), HttpStatusCode.Accepted);
            first.GetProperty("status").GetString().Should().Be(AppNoticeStatusConstants.Sent);
            var id = first.GetProperty("id").GetGuid();
            var again = await DeliveryAsync(await SendAsync(http, token, notice), HttpStatusCode.Accepted);
            again.GetProperty("id").GetGuid().Should().Be(id, "a repeated key answers the first outcome");

            var sent = bot.To(OwnerChat).Should().ContainSingle("one key sends once").Which;
            sent.ParseMode.Should().Be(ParseMode.Html);
            sent.Text.Should().Be(notice.Text);
            var rows = ((InlineKeyboardMarkup)sent.ReplyMarkup!).InlineKeyboard.Select(row => row.Single()).ToList();
            rows.Select(button => button.Url).Should().Equal("https://events.example.test/e/1", null);
            rows[1].Text.Should().Be("Bu bildirishnomalarni o'chirish", "the mute button speaks the notice's culture");
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var record = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().TelegramMessages.SingleAsync(m => m.Id == id);
                (record.Kind, record.UserId, record.DeleteAt).Should().Be((AppNoticeService.Kind, (Guid?)owner, (DateTimeOffset?)null), "an app notice stays in the chat");
            }

            (await CodeOfAsync(await SendAsync(http, token, notice with { Text = "Another text." }), HttpStatusCode.Conflict)).Should().Be(AppNoticeErrorCodeConstants.IdempotencyKeyReused);
            (await CodeOfAsync(await SendAsync(http, token, Notice(unlinked, "Hello.", "digest-1:unlinked")), HttpStatusCode.NotFound)).Should().Be(AppNoticeErrorCodeConstants.NotLinked);
            (await CodeOfAsync(await SendAsync(http, token, Notice(Guid.NewGuid(), "Hello.", "digest-1:nobody")), HttpStatusCode.NotFound)).Should().Be(AppNoticeErrorCodeConstants.NotLinked);
            (await CodeOfAsync(await SendAsync(http, token, Notice(owner, "<b>unclosed", "digest-1:broken")), HttpStatusCode.BadRequest)).Should().Be(AppNoticeErrorCodeConstants.Invalid);
            bot.To(OwnerChat).Should().ContainSingle();
        }
        finally
        {
            await PostgresTestServer.DropDatabaseAsync(connectionString);
        }
    }

    [Fact]
    public async Task MutedPerson_ShouldGetNoNotices_ButStillGetTheirSignInCode()
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync();
        try
        {
            var bot = new FakeTelegramBotClient();
            await using var host = CreateHost(connectionString, bot);
            using var http = Client(host);
            var owner = await CreatePersonAsync(host, OwnerPhone, OwnerChat);
            var member = await CreatePersonAsync(host, MemberPhone, MemberChat);
            var token = await TokenAsync(http, Events, AppNoticeConstants.Scope);

            // The mute button on a notice mutes that app for the person.
            await DeliveryAsync(await SendAsync(http, token, Notice(owner, "First digest.", "digest-1:owner")), HttpStatusCode.Accepted);
            var mute = ((InlineKeyboardMarkup)bot.To(OwnerChat).Single().ReplyMarkup!).InlineKeyboard.Select(row => row.Single()).Single(button => button.CallbackData is not null);
            (await PostUpdateAsync(http, CallbackUpdate(mute.CallbackData!, OwnerChat))).StatusCode.Should().Be(HttpStatusCode.OK);
            bot.Answers.Should().ContainSingle().Which.Text.Should().Contain("/start");

            var muted = await DeliveryAsync(await SendAsync(http, token, Notice(owner, "Second digest.", "digest-2:owner")), HttpStatusCode.OK);
            muted.GetProperty("status").GetString().Should().Be(AppNoticeStatusConstants.Muted);
            (!muted.TryGetProperty("id", out var mutedId) || mutedId.ValueKind == JsonValueKind.Null).Should().BeTrue("nothing was sent");
            bot.To(OwnerChat).Should().ContainSingle("a muted person gets nothing");

            // An access code still reaches the muted account.
            using (var code = await http.PostAsJsonAsync("/api/account/phone-sign-in/send", new { phone = OwnerPhone }))
                code.StatusCode.Should().Be(HttpStatusCode.OK, await code.Content.ReadAsStringAsync());
            bot.LastCode(OwnerChat).Should().MatchRegex("^[0-9]{6}$");
            bot.To(OwnerChat).Should().HaveCount(2, "the code arrives beside the one notice");

            // /stop mutes every app for the sender's person; a bare /start turns them on again.
            (await PostUpdateAsync(http, MessageUpdate(MemberChat, "/stop"))).StatusCode.Should().Be(HttpStatusCode.OK);
            bot.To(MemberChat).Last().Text.Should().Contain("Codes and security alerts still come");
            (await DeliveryAsync(await SendAsync(http, token, Notice(member, "Digest.", "digest-1:member")), HttpStatusCode.OK))
                .GetProperty("status").GetString().Should().Be(AppNoticeStatusConstants.Muted);
            bot.To(MemberChat).Should().ContainSingle("only the bot's answer to /stop");
            (await PostUpdateAsync(http, MessageUpdate(MemberChat, "/start"))).StatusCode.Should().Be(HttpStatusCode.OK);
            bot.To(MemberChat).Last().Text.Should().Be("App notices are on again.");
            (await DeliveryAsync(await SendAsync(http, token, Notice(member, "Digest.", "digest-2:member")), HttpStatusCode.Accepted))
                .GetProperty("status").GetString().Should().Be(AppNoticeStatusConstants.Sent);
            bot.To(MemberChat).Last().Text.Should().Be("Digest.");
        }
        finally
        {
            await PostgresTestServer.DropDatabaseAsync(connectionString);
        }
    }

    [Fact]
    public async Task ClientPastItsRateLimit_ShouldGet429_WithRetryAfter()
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync();
        try
        {
            var bot = new FakeTelegramBotClient();
            await using var host = CreateHost(connectionString, bot, new Dictionary<string, string?> { ["Identity:Notices:PermitLimit"] = "2" });
            using var http = Client(host);
            var unlinked = await CreatePersonAsync(host, "+998901111111", chatId: null);
            var token = await TokenAsync(http, Events, AppNoticeConstants.Scope);

            for (var i = 0; i < 2; i++)
                (await SendAsync(http, token, Notice(unlinked, "Hello.", $"limit-{i}"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
            using var limited = await SendAsync(http, token, Notice(unlinked, "Hello.", "limit-2"));

            limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            limited.Headers.RetryAfter!.Delta.Should().BePositive();
            (await CodeOfAsync(limited, HttpStatusCode.TooManyRequests)).Should().Be(AppNoticeErrorCodeConstants.RateLimited);
        }
        finally
        {
            await PostgresTestServer.DropDatabaseAsync(connectionString);
        }
    }

    /// <summary>The service with the bot on, a fake bot client, and two service clients on the notice resource.</summary>
    internal static WebApiTestHost<Program> CreateHost(string connectionString, FakeTelegramBotClient bot, IReadOnlyDictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["DatabaseSettings:ConnectionString"] = connectionString,
            ["AllowedHosts"] = "localhost",
            ["Logging:LogLevel:Default"] = "Warning",
            ["Identity:Server:Issuer"] = "https://localhost/",
            ["Identity:Notices:Audience"] = Audience,
            ["Identity:Server:Resources:0:Name"] = "identity",
            ["Identity:Server:Resources:0:Uri"] = Audience,
            ["Identity:Server:Resources:0:Scopes:0"] = AppNoticeConstants.Scope,
            ["Identity:Server:Resources:0:Scopes:1"] = "identity.people.read",
            ["Identity:Server:Clients:0:ClientId"] = Events,
            ["Identity:Server:Clients:0:ClientSecret"] = Secret,
            ["Identity:Server:Clients:0:Grants:0"] = "client_credentials",
            ["Identity:Server:Clients:0:Resources:0"] = "identity",
            ["Identity:Server:Clients:0:Scopes:0"] = AppNoticeConstants.Scope,
            ["Identity:Server:Clients:1:ClientId"] = Reader,
            ["Identity:Server:Clients:1:ClientSecret"] = Secret,
            ["Identity:Server:Clients:1:Grants:0"] = "client_credentials",
            ["Identity:Server:Clients:1:Resources:0"] = "identity",
            ["Identity:Server:Clients:1:Scopes:0"] = "identity.people.read",
            ["Identity:SignUp:Mode"] = "Invite",
            ["Identity:Methods:Password:IsEnabled"] = "false",
            ["Identity:Methods:Email:IsEnabled"] = "false",
            ["Identity:Telegram:IsEnabled"] = "true",
            ["Identity:Telegram:BotToken"] = "123456:E2E-token",
            ["Identity:Telegram:BotUserName"] = BotUserName,
            ["Identity:Telegram:WebhookSecret"] = HookSecret,
            ["Identity:Phone:AllowedCountries:0"] = "UZ",
            ["Identity:Otp:HashKey"] = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
            ["Identity:Otp:OriginRateLimitWindow"] = "00:00:00",
        };
        foreach (var (key, value) in extra ?? new Dictionary<string, string?>())
            settings[key] = value;

        var host = new WebApiTestHost<Program>
        {
            ConfigureConfigurationHook = configuration => configuration.AddInMemoryCollection(settings),
            ConfigureServicesHook = services =>
            {
                services.RemoveAll<ITelegramBotClient>();
                services.AddSingleton<ITelegramBotClient>(bot);
            },
        };
        host.Clock.SetUtcNow(DateTimeOffset.UtcNow);
        return host;
    }

    /// <summary>A person holding <paramref name="phone"/> confirmed, linked to <paramref name="chatId"/> unless it is null.</summary>
    internal static async Task<Guid> CreatePersonAsync(WebApiTestHost<Program> host, string phone, long? chatId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var person = new IdentityUser { UserName = phone, DisplayName = phone, PhoneNumber = phone, IsPhoneNumberConfirmed = true };
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

    internal static HttpClient Client(WebApiTestHost<Program> host)
        => host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });

    internal static async Task<string> TokenAsync(HttpClient http, string clientId, string scope)
    {
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = Secret,
            ["resource"] = Audience,
            ["scope"] = scope,
        });
        using var response = await http.PostAsync("/connect/token", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;
    }

    private static AppNotice Notice(Guid sub, string text, string key) => new()
    {
        Subject = sub.ToString(),
        Text = text,
        Buttons = [new AppNoticeButton { Text = "Open", Url = "https://events.example.test/e/1" }],
        IdempotencyKey = key,
        Culture = "uz-Latn",
    };

    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, string? token, AppNotice notice)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AppNoticeConstants.Path) { Content = JsonContent.Create(notice) };
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request);
    }

    private static async Task<JsonElement> DeliveryAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(expected, body);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.GetProperty("data").Clone();
        }
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(expected, body);
            using var problem = JsonDocument.Parse(body);
            return problem.RootElement.TryGetProperty("identityCode", out var code) ? code.GetString() : body;
        }
    }

    private static async Task<HttpResponseMessage> PostUpdateAsync(HttpClient client, string update)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/telegram/webhook") { Content = new StringContent(update, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", HookSecret);
        return await client.SendAsync(request);
    }

    private static string MessageUpdate(long chatId, string text) => $$"""
        {
          "update_id": 1,
          "message": {
            "message_id": 1,
            "date": 1793520000,
            "chat": { "id": {{chatId}}, "type": "private", "first_name": "Member" },
            "from": { "id": {{chatId}}, "is_bot": false, "first_name": "Member", "language_code": "en" },
            "text": "{{text}}"
          }
        }
        """;

    private static string CallbackUpdate(string data, long fromId) => $$"""
        {
          "update_id": 2,
          "callback_query": {
            "id": "press-1",
            "from": { "id": {{fromId}}, "is_bot": false, "first_name": "Owner", "language_code": "en" },
            "chat_instance": "1",
            "data": "{{data}}"
          }
        }
        """;
}

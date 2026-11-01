using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Identity.Tests.E2E.PhoneSignIn;

/// <summary>
/// How the real service receives the bot's updates: a local run polls and signs a person in through the same handler the
/// webhook uses (the webhook answering 404 meanwhile), the webhook address follows the issuer, a local run registers no
/// webhook, and an unknown mode stops the boot. (A deployed boot's refusal of polling is in the unit tier: a deployed
/// boot of this host needs a migrated, claimed schema and real keys.)
/// </summary>
public sealed partial class BotUpdateModeTests
{
    private const string Phone = "+998901234567";
    private const long ChatId = 7070;
    private const string Secret = "hook-secret_modes";

    [Fact]
    public async Task Polling_ShouldLinkTheBotAndSignThePersonIn_WithoutAWebhook()
    {
        var bot = new ScriptedTelegramBotClient();
        var connectionString = await PostgresTestServer.CreateDatabaseAsync();
        try
        {
            await using var host = CreateHost(connectionString, bot, new Dictionary<string, string?> { ["Identity:Telegram:UpdateMode"] = "Polling" });
            using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

            using var send = await client.PostAsJsonAsync("/api/account/phone-sign-in/send", new { phone = Phone });
            var url = (await DataAsync(send)).GetProperty("telegramLinkUrl").GetString()!;
            bot.Enqueue(Update(1, message => message.Text = "/start " + url[(url.IndexOf('=') + 1)..]));
            await EventuallyAsync(() => bot.Sent.Count == 1);
            bot.Enqueue(Update(2, message => message.Contact = new Contact { PhoneNumber = Phone, FirstName = "Lola", UserId = ChatId }));
            await EventuallyAsync(() => bot.Sent.Count == 2);

            var code = CodePattern().Match(bot.Sent.Last().Text).Value;
            using var signedIn = await client.PostAsJsonAsync("/api/account/phone-sign-in?useCookies=true", new { phone = Phone, code });
            signedIn.StatusCode.Should().Be(HttpStatusCode.NoContent);
            bot.Methods.Should().NotContain("deleteWebhook").And.NotContain("setWebhook").And.NotContain("getWebhookInfo");
            await using var scope = host.Services.CreateAsyncScope();
            (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().TelegramLinks.SingleAsync()).ChatId.Should().Be(ChatId);

            using var webhook = new HttpRequestMessage(HttpMethod.Post, "/api/telegram/webhook") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            webhook.Headers.Add("X-Telegram-Bot-Api-Secret-Token", Secret);
            (await client.SendAsync(webhook)).StatusCode.Should().Be(HttpStatusCode.NotFound, "a polling instance has no webhook to serve");
        }
        finally
        {
            await PostgresTestServer.DropDatabaseAsync(connectionString);
        }
    }

    [Fact]
    public async Task Webhook_ShouldFollowTheIssuer_UnlessASettingNamesAnotherAddress_AndLocalRunsRegisterNothing()
    {
        var bot = new ScriptedTelegramBotClient();
        var connectionString = await PostgresTestServer.CreateDatabaseAsync();
        try
        {
            await using var derived = CreateHost(connectionString, bot, new Dictionary<string, string?> { ["Identity:Server:Issuer"] = "https://id.example.test/" });
            using var client = derived.CreateClient();
            derived.Services.GetRequiredService<IOptionsMonitor<TelegramBotOptions>>().CurrentValue.WebhookUrl
                .Should().Be("https://id.example.test/api/telegram/webhook");

            await using var named = CreateHost(connectionString, bot, new Dictionary<string, string?>
            {
                ["Identity:Server:Issuer"] = "https://id.example.test/",
                ["Identity:Telegram:WebhookUrl"] = "https://bot.example.test/hook",
            });
            using var other = named.CreateClient();
            named.Services.GetRequiredService<IOptionsMonitor<TelegramBotOptions>>().CurrentValue.WebhookUrl.Should().Be("https://bot.example.test/hook");

            await Task.Delay(100);
            bot.Methods.Should().BeEmpty("a local run has no public address, so nothing is registered or polled");
        }
        finally
        {
            await PostgresTestServer.DropDatabaseAsync(connectionString);
        }
    }

    [Fact]
    public async Task Boot_ShouldStop_WhenTheUpdateModeIsUnknown()
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync();
        try
        {
            await using var host = CreateHost(connectionString, new ScriptedTelegramBotClient(), new Dictionary<string, string?> { ["Identity:Telegram:UpdateMode"] = "Sideways" });

            FluentActions.Invoking(() => host.CreateClient()).Should().Throw<Exception>().Where(e => e.ToString().Contains("UpdateMode"));
        }
        finally
        {
            await PostgresTestServer.DropDatabaseAsync(connectionString);
        }
    }

    private static WebApiTestHost<Program> CreateHost(string connectionString, ScriptedTelegramBotClient bot, IReadOnlyDictionary<string, string?> settings)
    {
        var values = new Dictionary<string, string?>
        {
            ["DatabaseSettings:ConnectionString"] = connectionString,
            ["AllowedHosts"] = "localhost",
            ["Logging:LogLevel:Default"] = "Warning",
            ["Identity:SignUp:IsEnabled"] = "true",
            ["Identity:Telegram:IsEnabled"] = "true",
            ["Identity:Telegram:BotToken"] = "123456:E2E-token",
            ["Identity:Telegram:BotUserName"] = "ocharo_e2e_bot",
            ["Identity:Telegram:WebhookSecret"] = Secret,
            ["Identity:Phone:AllowedCountries:0"] = "UZ",
            ["Identity:Otp:HashKey"] = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
        };
        foreach (var (key, value) in settings)
            values[key] = value;

        var host = new WebApiTestHost<Program>
        {
            ConfigureConfigurationHook = configuration => configuration.AddInMemoryCollection(values),
            ConfigureServicesHook = services =>
            {
                services.RemoveAll<ITelegramBotClient>();
                services.AddSingleton<ITelegramBotClient>(bot);
            },
        };
        host.Clock.SetUtcNow(DateTimeOffset.UtcNow);
        return host;
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("data").Clone();
    }

    private static Update Update(int id, Action<Message> shape)
    {
        var message = new Message
        {
            Id = id,
            Date = DateTime.UtcNow,
            Chat = new Chat { Id = ChatId, Type = ChatType.Private },
            From = new User { Id = ChatId, IsBot = false, FirstName = "Lola", LanguageCode = "en" },
        };
        shape(message);
        return new Update { Id = id, Message = message };
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The condition did not hold within twenty seconds.");

            await Task.Delay(20);
        }
    }

    [GeneratedRegex("[0-9]{6}")]
    private static partial Regex CodePattern();
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using AwesomeAssertions;
using Identity.Tests.E2E.PhoneSignIn;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Telegram.Bot;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Identity.Tests.E2E.Accounts;

/// <summary>
/// A member's pending Telegram account change through the real API and the bot's webhook with a fake bot: a newcomer
/// sharing a member's linked number leaves the link where it is and flags the member, an owner or admin approves (the
/// link moves) or denies (the request goes), and the routes refuse outsiders, non-managers, admins acting on owners,
/// non-members and members with nothing pending.
/// </summary>
public sealed class TelegramChangeApiTests
{
    private const string Phone = "+998901234567";
    private const string Secret = "hook-secret_change";
    private const string BotName = "ocharo_change_bot";
    private const long OldChat = 6161;
    private const long NewChat = 7272;
    private const string Password = "correct horse battery";

    [Fact]
    public async Task Approve_ShouldMoveTheMembersLinkToTheNewAccount_Once()
    {
        await using var test = await ChangeTest.CreateAsync();
        var lola = await test.MemberWithLinkAsync("lola@example.test", "editor");
        await test.NewcomerSharesAsync();

        var members = await AccountHost.DataAsync(await test.Owner.GetAsync(test.Members()));
        members.EnumerateArray().Select(m => (m.GetProperty("email").GetString(), m.GetProperty("hasPendingTelegramChange").GetBoolean()))
            .Should().BeEquivalentTo([("owner@example.test", false), ("lola@example.test", true)]);
        test.Bot.To(NewChat).Last().Text.Should().Contain("linked to another Telegram account");
        (await test.LinkAsync()).TelegramUserId.Should().Be(OldChat, "the link waits for approval");

        (await test.Owner.PostAsync(test.Members(lola, "approve"), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await test.LinkAsync()).Should().BeEquivalentTo(new { ChatId = NewChat, TelegramUserId = NewChat });
        (await AccountHost.DataAsync(await test.Owner.GetAsync(test.Members()))).EnumerateArray()
            .Should().OnlyContain(m => !m.GetProperty("hasPendingTelegramChange").GetBoolean());
        await ExpectAsync(await test.Owner.PostAsync(test.Members(lola, "approve"), null), HttpStatusCode.NotFound, "TelegramChangeNotFound");
    }

    [Fact]
    public async Task Deny_ShouldDropTheRequest_AndKeepTheLink()
    {
        await using var test = await ChangeTest.CreateAsync();
        var lola = await test.MemberWithLinkAsync("lola@example.test", "editor");
        await test.NewcomerSharesAsync();
        var admin = await test.SignedInMemberAsync("admin@example.test", "admin");

        (await admin.Client.PostAsync(test.Members(lola, "deny"), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await test.LinkAsync()).TelegramUserId.Should().Be(OldChat);
        await using (var scope = test.Host.Services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().TelegramLinkChanges.AnyAsync()).Should().BeFalse();
        await ExpectAsync(await admin.Client.PostAsync(test.Members(lola, "deny"), null), HttpStatusCode.NotFound, "TelegramChangeNotFound");
        await ExpectAsync(await admin.Client.PostAsync(test.Members(lola, "approve"), null), HttpStatusCode.NotFound, "TelegramChangeNotFound");
    }

    [Fact]
    public async Task Routes_ShouldRefuseWhoMayNotResolveIt()
    {
        await using var test = await ChangeTest.CreateAsync();
        var lola = await test.MemberWithLinkAsync("lola@example.test", "editor");
        await test.NewcomerSharesAsync();
        var editor = await test.SignedInMemberAsync("editor@example.test", "editor");
        var admin = await test.SignedInMemberAsync("admin@example.test", "admin");
        var outsider = await test.SignedInMemberAsync("outsider@example.test", role: null);

        (await test.Client().PostAsync(test.Members(lola, "approve"), null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await ExpectAsync(await editor.Client.PostAsync(test.Members(lola, "approve"), null), HttpStatusCode.Forbidden, "OrganizationManagerRequired");
        await ExpectAsync(await outsider.Client.PostAsync(test.Members(lola, "deny"), null), HttpStatusCode.NotFound, "OrganizationNotFound");
        await ExpectAsync(await admin.Client.PostAsync(test.Members(outsider.UserId, "approve"), null), HttpStatusCode.NotFound, "UserNotMember");
        await ExpectAsync(await admin.Client.PostAsync(test.Members(test.OwnerId, "approve"), null), HttpStatusCode.Forbidden, "OrganizationOwnerRequired");
        await ExpectAsync(await admin.Client.PostAsync(test.Members(editor.UserId, "deny"), null), HttpStatusCode.NotFound, "TelegramChangeNotFound");

        (await test.LinkAsync()).TelegramUserId.Should().Be(OldChat, "no refused call moved the link");
        await using var scope = test.Host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<TelegramLinkChangeService>().IsPendingAsync(Phone)).Should().BeTrue("no refused call dropped the request");
    }

    private static async Task ExpectAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
            (await AccountHost.ProblemAsync(response)).GetProperty("identityCode").GetString().Should().Be(code);
        }
    }

    /// <summary>The API with the bot on over a fake Telegram, an organization and its signed-in owner.</summary>
    private sealed class ChangeTest : IAsyncDisposable
    {
        private readonly string _connectionString;

        private ChangeTest(string connectionString, WebApiTestHost<Program> host, FakeTelegramBotClient bot)
        {
            _connectionString = connectionString;
            Host = host;
            Bot = bot;
        }

        public WebApiTestHost<Program> Host { get; }

        public FakeTelegramBotClient Bot { get; }

        public HttpClient Owner { get; private set; } = null!;

        public Guid OwnerId { get; private set; }

        public Guid OrganizationId { get; private set; }

        public static async Task<ChangeTest> CreateAsync()
        {
            var connectionString = await PostgresTestServer.CreateDatabaseAsync();
            var bot = new FakeTelegramBotClient();
            var host = new WebApiTestHost<Program>
            {
                ConfigureConfigurationHook = configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DatabaseSettings:ConnectionString"] = connectionString,
                    ["AllowedHosts"] = "localhost",
                    ["Logging:LogLevel:Default"] = "Warning",
                    ["Identity:SignUp:Mode"] = "Invite",
                    ["Identity:Server:Issuer"] = AccountHost.Issuer,
                    ["Identity:Telegram:IsEnabled"] = "true",
                    ["Identity:Telegram:BotToken"] = "123456:E2E-token",
                    ["Identity:Telegram:BotUserName"] = BotName,
                    ["Identity:Telegram:WebhookSecret"] = Secret,
                    ["Identity:Phone:AllowedCountries:0"] = "UZ",
                    ["Identity:Organizations:Roles:admin:0"] = "members:manage",
                    ["Identity:Organizations:Roles:editor:0"] = "content:edit",
                    ["Identity:Otp:HashKey"] = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
                }),
                ConfigureServicesHook = services =>
                {
                    services.RemoveAll<ITelegramBotClient>();
                    services.AddSingleton<ITelegramBotClient>(bot);
                },
            };
            host.Clock.SetUtcNow(DateTimeOffset.UtcNow);
            var test = new ChangeTest(connectionString, host, bot);

            test.OwnerId = await test.CreateUserAsync("owner@example.test");
            var organization = new IdentityOrganization { Name = "Ocharo", Slug = "ocharo" };
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var owner = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(test.OwnerId))!;
                (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateAsync(organization, owner)).IsSuccess.Should().BeTrue();
            }

            test.OrganizationId = organization.Id;
            test.Owner = await test.SignInAsync("owner@example.test");
            return test;
        }

        /// <summary>The member route of <paramref name="userId"/>, or the member list; <paramref name="decision"/> names the change's route.</summary>
        public string Members(Guid? userId = null, string? decision = null)
            => $"/api/organizations/{OrganizationId}/members"
                + (userId is { } id ? $"/{id}" : string.Empty)
                + (decision is null ? string.Empty : $"/telegram-change/{decision}");

        /// <summary>A member holding <see cref="Phone"/> confirmed, its link to <see cref="OldChat"/> already made.</summary>
        public async Task<Guid> MemberWithLinkAsync(string email, string role)
        {
            var userId = await CreateUserAsync(email, Phone);
            await JoinAsync(userId, role);
            await using var scope = Host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ITelegramLinkRepository>().SaveLinkAsync(new IdentityTelegramLink
            {
                Id = Guid.CreateVersion7(),
                BotUserName = BotName,
                PhoneNumber = Phone,
                ChatId = OldChat,
                TelegramUserId = OldChat,
                LinkedAt = Host.Clock.GetUtcNow(),
            });
            return userId;
        }

        /// <summary>A newcomer in <see cref="NewChat"/> opens a start link for <see cref="Phone"/> and shares it through the webhook.</summary>
        public async Task NewcomerSharesAsync()
        {
            string url;
            await using (var scope = Host.Services.CreateAsyncScope())
                url = await scope.ServiceProvider.GetRequiredService<TelegramLinkService>().CreateStartLinkAsync(Phone, TelegramLinkPurposeConstants.PhoneSignIn, origin: null);

            using var client = Client();
            (await PostUpdateAsync(client, $"\"text\": \"/start {url[(url.IndexOf('=') + 1)..]}\"")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await PostUpdateAsync(client, $"\"contact\": {{ \"phone_number\": \"{Phone}\", \"first_name\": \"Lola\", \"user_id\": {NewChat} }}")).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        /// <summary>A person with a password, signed in, and in the organization with <paramref name="role"/> unless it is null.</summary>
        public async Task<(Guid UserId, HttpClient Client)> SignedInMemberAsync(string email, string? role)
        {
            var userId = await CreateUserAsync(email);
            if (role is not null)
                await JoinAsync(userId, role);
            return (userId, await SignInAsync(email));
        }

        /// <summary>The number's link on the bot.</summary>
        public async Task<IdentityTelegramLink> LinkAsync()
        {
            await using var scope = Host.Services.CreateAsyncScope();
            return (await scope.ServiceProvider.GetRequiredService<ITelegramLinkRepository>().FindLinkAsync(BotName, Phone))!;
        }

        public async ValueTask DisposeAsync()
        {
            Owner?.Dispose();
            await Host.DisposeAsync();
            await PostgresTestServer.DropDatabaseAsync(_connectionString);
        }

        private async Task<Guid> CreateUserAsync(string email, string? confirmedPhone = null)
        {
            await using var scope = Host.Services.CreateAsyncScope();
            var user = new IdentityUser
            {
                UserName = email,
                Email = email,
                IsEmailConfirmed = true,
                PhoneNumber = confirmedPhone,
                IsPhoneNumberConfirmed = confirmedPhone is not null,
            };
            var created = await scope.ServiceProvider.GetRequiredService<UserPasswordService>().CreateWithPasswordAsync(user, Password);
            created.IsSuccess.Should().BeTrue(string.Join("; ", created.Errors.Select(error => error.Code)));
            return user.Id;
        }

        private async Task JoinAsync(Guid userId, string role)
        {
            await using var scope = Host.Services.CreateAsyncScope();
            var organizations = scope.ServiceProvider.GetRequiredService<OrganizationService>();
            var organization = (await organizations.FindByIdAsync(OrganizationId))!;
            var user = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(userId))!;
            (await organizations.AddMemberAsync(organization, user, role)).IsSuccess.Should().BeTrue();
        }

        /// <summary>A browser-like client with no session: HTTPS, cookies kept, redirects returned.</summary>
        public HttpClient Client()
            => Host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });

        private async Task<HttpClient> SignInAsync(string email)
        {
            var client = Client();
            (await AccountHost.SignInAsync(client, email, Password)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            return client;
        }

        private static async Task<HttpResponseMessage> PostUpdateAsync(HttpClient client, string body)
        {
            var update = $$"""
                {
                  "update_id": 1,
                  "message": {
                    "message_id": 1,
                    "date": 1793520000,
                    "chat": { "id": {{NewChat}}, "type": "private", "first_name": "Lola" },
                    "from": { "id": {{NewChat}}, "is_bot": false, "first_name": "Lola", "language_code": "en" },
                    {{body}}
                  }
                }
                """;
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/telegram/webhook") { Content = new StringContent(update, Encoding.UTF8, "application/json") };
            request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", Secret);
            return await client.SendAsync(request);
        }
    }
}

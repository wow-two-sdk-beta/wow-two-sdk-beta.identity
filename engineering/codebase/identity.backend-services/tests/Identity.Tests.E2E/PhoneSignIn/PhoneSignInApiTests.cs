using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Telegram.Bot;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Identity.Tests.E2E.PhoneSignIn;

/// <summary>
/// Sign-in by phone through the real service with a fake bot: <c>phone-sign-in/send</c>, the bot's webhook with its
/// secret header, the start link and the shared contact, then <c>phone-sign-in</c> with the code the bot sent, ending in
/// the <c>__Host-identity</c> cookie, a session with <c>amr</c> <c>otp</c> and a <c>phone_code</c> sign-in log row.
/// </summary>
[Collection(IdentityApiCollection.Name)]
public sealed class PhoneSignInApiTests(IdentityApiFixture fixture)
{
    private const string Phone = "+998901234567";
    private const string Secret = "hook-secret_e2e";
    private const long ChatId = 5151;

    [Fact]
    public async Task FirstSignIn_ShouldLinkTheBot_CreateThePerson_AndOpenACookieSession()
    {
        var bot = new FakeTelegramBotClient();
        var connectionString = await PostgresTestServer.CreateDatabaseAsync();
        try
        {
            await using var host = CreateHost(connectionString, bot, isSignUpOpen: true);
            using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

            var link = await SendAsync(client, Phone);
            link.GetProperty("isCodeSent").GetBoolean().Should().BeFalse();
            var url = link.GetProperty("telegramLinkUrl").GetString()!;
            url.Should().StartWith("https://t.me/ocharo_e2e_bot?start=");

            using (var forged = await PostUpdateAsync(client, StartUpdate(url), secret: "wrong"))
                forged.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            bot.Sent.Should().BeEmpty("a forged update reaches nothing");

            (await PostUpdateAsync(client, StartUpdate(url))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await PostUpdateAsync(client, ContactUpdate("998901234567"))).StatusCode.Should().Be(HttpStatusCode.OK);
            var code = bot.LastCode(ChatId);

            using (var wrong = await client.PostAsJsonAsync("/api/account/phone-sign-in?useCookies=true", new { phone = Phone, code = code == "000000" ? "111111" : "000000" }))
            {
                wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
                (await IdentityCodeAsync(wrong)).Should().Be("InvalidPhoneCode");
            }

            using var signedIn = await client.PostAsJsonAsync("/api/account/phone-sign-in?useCookies=true&clientId=ocharo-web", new { phone = Phone, code });
            signedIn.StatusCode.Should().Be(HttpStatusCode.NoContent);
            signedIn.Headers.GetValues("Set-Cookie").Should().Contain(cookie => cookie.StartsWith("__Host-identity="));
            (await client.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.OK);

            await using (var scope = host.Services.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var person = await context.Users.SingleAsync(u => u.PhoneNumber == Phone);
                person.IsPhoneNumberConfirmed.Should().BeTrue();
                (await context.Sessions.SingleAsync(s => s.UserId == person.Id)).AuthenticationMethods.Should().Equal("otp");
                var logged = await context.SignInLog.Where(e => e.Method == "phone_code").OrderBy(e => e.Id).ToListAsync();
                logged.Select(e => e.Result).Should().Equal("failed", "succeeded");
                logged[^1].ClientId.Should().Be("ocharo-web");
                logged[^1].UserId.Should().Be(person.Id);
            }

            host.Clock.Advance(TimeSpan.FromSeconds(10));
            var linked = await SendAsync(client, Phone);
            linked.GetProperty("isCodeSent").GetBoolean().Should().BeTrue("the number is linked now");
            linked.GetProperty("telegramLinkUrl").ValueKind.Should().Be(JsonValueKind.Null);
            bot.LastCode(ChatId).Should().NotBe(code);
        }
        finally
        {
            await PostgresTestServer.DropDatabaseAsync(connectionString);
        }
    }

    [Fact]
    public async Task FirstSignIn_ShouldAnswer403_WhileSignUpIsOff_AndRefuseOtherCountriesAndRepeats()
    {
        var bot = new FakeTelegramBotClient();
        var connectionString = await PostgresTestServer.CreateDatabaseAsync();
        try
        {
            await using var host = CreateHost(connectionString, bot, isSignUpOpen: false);
            using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

            using (var foreign = await client.PostAsJsonAsync("/api/account/phone-sign-in/send", new { phone = "+77011234567" }))
            {
                foreign.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                (await IdentityCodeAsync(foreign)).Should().Be("PhoneCountryNotAllowed");
            }

            var url = (await SendAsync(client, Phone)).GetProperty("telegramLinkUrl").GetString()!;
            using (var repeat = await client.PostAsJsonAsync("/api/account/phone-sign-in/send", new { phone = Phone }))
            {
                repeat.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
                (await IdentityCodeAsync(repeat)).Should().Be("PhoneCodeRateLimited");
            }

            (await PostUpdateAsync(client, StartUpdate(url), secret: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await PostUpdateAsync(client, StartUpdate(url))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await PostUpdateAsync(client, ContactUpdate(Phone))).StatusCode.Should().Be(HttpStatusCode.OK);

            using (var bearer = await client.PostAsJsonAsync("/api/account/phone-sign-in", new { phone = Phone, code = bot.LastCode(ChatId) }))
            {
                bearer.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the instance signs in by cookie only");
                (await IdentityCodeAsync(bearer)).Should().Be("BearerSignInUnsupported");
            }

            using var closed = await client.PostAsJsonAsync("/api/account/phone-sign-in?useCookies=true", new { phone = Phone, code = bot.LastCode(ChatId) });
            closed.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the refused bearer try left the code unspent");
            (await IdentityCodeAsync(closed)).Should().Be("SignUpClosed");
            await using var scope = host.Services.CreateAsyncScope();
            (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.AnyAsync()).Should().BeFalse();
        }
        finally
        {
            await PostgresTestServer.DropDatabaseAsync(connectionString);
        }
    }

    [Fact]
    public async Task FirstSignIn_ShouldCreateThePersonIntoTheInvitationsRole_WhileSignUpIsByInvitation()
    {
        var bot = new FakeTelegramBotClient();
        var connectionString = await PostgresTestServer.CreateDatabaseAsync();
        try
        {
            await using var host = CreateHost(connectionString, bot, isSignUpOpen: false);
            using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            Guid organizationId;
            string forOtherNumber, forThisNumber;
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var owner = new IdentityUser { UserName = "owner", Email = "owner@example.test" };
                (await scope.ServiceProvider.GetRequiredService<UserAccountService>().CreateAsync(owner)).IsSuccess.Should().BeTrue();
                var organization = new IdentityOrganization { Name = "Ocharo", Slug = "ocharo" };
                (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateAsync(organization, owner)).IsSuccess.Should().BeTrue();
                organizationId = organization.Id;
                var invitations = scope.ServiceProvider.GetRequiredService<InvitationService>();
                forOtherNumber = (await invitations.CreateAsync(organization, owner, "editor", "+998907654321", null)).Token!;
                forThisNumber = (await invitations.CreateAsync(organization, owner, "editor", Phone, null)).Token!;
            }

            var url = (await SendAsync(client, Phone)).GetProperty("telegramLinkUrl").GetString()!;
            (await PostUpdateAsync(client, StartUpdate(url))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await PostUpdateAsync(client, ContactUpdate(Phone))).StatusCode.Should().Be(HttpStatusCode.OK);
            var code = bot.LastCode(ChatId);

            using (var mismatch = await client.PostAsJsonAsync("/api/account/phone-sign-in?useCookies=true", new { phone = Phone, code, invitationToken = forOtherNumber }))
            {
                mismatch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
                (await IdentityCodeAsync(mismatch)).Should().Be("InvitationIdentifierMismatch");
            }

            using var signedIn = await client.PostAsJsonAsync("/api/account/phone-sign-in?useCookies=true", new { phone = Phone, code, invitationToken = forThisNumber });
            signedIn.StatusCode.Should().Be(HttpStatusCode.NoContent, "the refused mismatch left the code unspent");
            await using var check = host.Services.CreateAsyncScope();
            var context = check.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var person = await context.Users.SingleAsync(u => u.PhoneNumber == Phone);
            (await context.Memberships.SingleAsync(m => m.UserId == person.Id)).Should().Match<IdentityMembership>(m => m.OrganizationId == organizationId && m.Role == "editor");
            (await context.Invitations.SingleAsync(i => i.AcceptedByUserId == person.Id)).PhoneNumber.Should().Be(Phone);
        }
        finally
        {
            await PostgresTestServer.DropDatabaseAsync(connectionString);
        }
    }

    [Fact]
    public async Task PhoneSignIn_ShouldAnswer404_WhileTheBotIsOff()
    {
        using var client = fixture.Host.CreateClient();

        (await client.PostAsJsonAsync("/api/account/phone-sign-in/send", new { phone = Phone })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync("/api/account/phone-sign-in", new { phone = Phone, code = "123456" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var webhook = new HttpRequestMessage(HttpMethod.Post, "/api/telegram/webhook") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        (await client.SendAsync(webhook)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Boot_ShouldStop_WhenTheBotIsOnWithoutItsToken()
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync();
        try
        {
            await using var host = CreateHost(connectionString, new FakeTelegramBotClient(), isSignUpOpen: false, botToken: null);

            FluentActions.Invoking(() => host.CreateClient()).Should().Throw<Exception>().Where(e => e.ToString().Contains("BotToken"));
        }
        finally
        {
            await PostgresTestServer.DropDatabaseAsync(connectionString);
        }
    }

    private static WebApiTestHost<Program> CreateHost(string connectionString, FakeTelegramBotClient bot, bool isSignUpOpen, string? botToken = "123456:E2E-token")
    {
        var host = new WebApiTestHost<Program>
        {
            ConfigureConfigurationHook = configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseSettings:ConnectionString"] = connectionString,
                ["AllowedHosts"] = "localhost",
                ["Logging:LogLevel:Default"] = "Warning",
                ["Identity:SignUp:Mode"] = isSignUpOpen ? "Open" : "Invite",
                ["Identity:Telegram:IsEnabled"] = "true",
                ["Identity:Telegram:BotToken"] = botToken,
                ["Identity:Telegram:BotUserName"] = "ocharo_e2e_bot",
                ["Identity:Telegram:WebhookSecret"] = Secret,
                ["Identity:Phone:AllowedCountries:0"] = "UZ",
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
        return host;
    }

    private static async Task<JsonElement> SendAsync(HttpClient client, string phone)
    {
        using var response = await client.PostAsJsonAsync("/api/account/phone-sign-in/send", new { phone });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("data").Clone();
    }

    private static async Task<string?> IdentityCodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("identityCode", out var code) ? code.GetString() : null;
    }

    private static async Task<HttpResponseMessage> PostUpdateAsync(HttpClient client, string update, string? secret = Secret)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/telegram/webhook") { Content = new StringContent(update, Encoding.UTF8, "application/json") };
        if (secret is not null)
            request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", secret);
        return await client.SendAsync(request);
    }

    private static string StartUpdate(string url) => Update($$"""
        "text": "/start {{url[(url.IndexOf('=') + 1)..]}}"
        """);

    private static string ContactUpdate(string phone) => Update($$"""
        "contact": { "phone_number": "{{phone}}", "first_name": "Lola", "user_id": {{ChatId}} }
        """);

    private static string Update(string body) => $$"""
        {
          "update_id": 1,
          "message": {
            "message_id": 1,
            "date": 1793520000,
            "chat": { "id": {{ChatId}}, "type": "private", "first_name": "Lola" },
            "from": { "id": {{ChatId}}, "is_bot": false, "first_name": "Lola", "language_code": "en" },
            {{body}}
          }
        }
        """;
}

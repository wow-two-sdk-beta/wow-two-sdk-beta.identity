using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SkiaSharp;
using Telegram.Bot;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Identity.Tests.E2E.PhoneSignIn;

/// <summary>
/// Sign-up from an invitation through the real service with a fake bot: the declaration asks for a name and a picture;
/// the start route checks both and answers the bot link; the shared contact proves the phone, creates the person and joins
/// the organization; the browser's poll signs it in once; the picture is served as WebP; the invitation is spent.
/// </summary>
public sealed class InviteSignUpApiTests
{
    private const string Phone = "+998901234567";
    private const string Secret = "hook-secret_e2e";
    private const long ChatId = 6161;

    [Fact]
    public async Task InviteSignUp_ShouldJoinThroughTheBot_SignTheBrowserInOnce_AndKeepThePicture()
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync();
        try
        {
            var bot = new FakeTelegramBotClient();
            await using var host = CreateHost(connectionString, bot);
            using var browser = Client(host);
            var (organizationId, token) = await InviteAsync(host);

            var options = await DataAsync(await browser.GetAsync("/api/account/sign-in-options"));
            var profile = options.GetProperty("signUpProfile");
            profile.GetProperty("displayName").GetString().Should().Be("required");
            profile.GetProperty("picture").GetString().Should().Be("required");
            profile.GetProperty("pictureRules").GetProperty("acceptedTypes").EnumerateArray().Select(type => type.GetString())
                .Should().Equal("image/jpeg", "image/png", "image/webp");

            (await CodeOfAsync(await StartAsync(browser, token, name: null, Png(400, 300)))).Should().Be("DisplayNameRequired");
            (await CodeOfAsync(await StartAsync(browser, token, "Maftuna", Encoding.ASCII.GetBytes("GIF89a......")))).Should().Be("PictureUnsupportedType");
            (await CodeOfAsync(await StartAsync(browser, token, "Maftuna", Png(100, 100)))).Should().Be("PictureTooSmall");

            using var started = await StartAsync(browser, token, " Maftuna Karimova ", Png(400, 300));
            var link = (await DataAsync(started)).GetProperty("telegramLinkUrl").GetString()!;
            link.Should().StartWith("https://t.me/ocharo_e2e_bot?start=");
            (await StatusAsync(browser)).GetProperty("status").GetString().Should().Be("pending");
            using (var stranger = Client(host))
                (await stranger.GetAsync("/api/account/invitations/sign-up/status")).StatusCode.Should().Be(HttpStatusCode.NotFound, "only the starting browser reads it");

            (await PostUpdateAsync(browser, StartUpdate(link))).StatusCode.Should().Be(HttpStatusCode.OK);
            bot.To(ChatId).Last().Text.Should().Contain("finish signing up");
            (await PostUpdateAsync(browser, ContactUpdate("998901234567"))).StatusCode.Should().Be(HttpStatusCode.OK);
            bot.To(ChatId).Last().Text.Should().Contain("Your number is verified");

            var done = await StatusAsync(browser);
            done.GetProperty("status").GetString().Should().Be("completed");
            done.GetProperty("isSignedIn").GetBoolean().Should().BeTrue();
            var info = await DataAsync(await browser.GetAsync("/api/account/manage/info"));
            var userId = Guid.Parse(info.GetProperty("userId").GetString()!);
            (await browser.GetAsync("/api/account/invitations/sign-up/status")).StatusCode.Should().Be(HttpStatusCode.NotFound, "a finished sign-up clears the cookie");

            await using (var scope = host.Services.CreateAsyncScope())
            {
                var person = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(userId))!;
                person.Should().BeEquivalentTo(new { DisplayName = "Maftuna Karimova", PhoneNumber = Phone, IsPhoneNumberConfirmed = true, Email = (string?)null }, o => o.ExcludingMissingMembers());
                var organizations = scope.ServiceProvider.GetRequiredService<OrganizationService>();
                (await organizations.GetRoleAsync((await organizations.FindByIdAsync(organizationId))!, person)).Should().Be("editor");
            }

            using var picture = await browser.GetAsync($"/api/account/pictures/{userId}");
            picture.StatusCode.Should().Be(HttpStatusCode.OK);
            picture.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
            var bytes = await picture.Content.ReadAsByteArrayAsync();
            Encoding.ASCII.GetString(bytes, 8, 4).Should().Be("WEBP");
            using (var decoded = SKBitmap.Decode(bytes))
                (decoded.Width, decoded.Height).Should().Be((512, 512));

            (await CodeOfAsync(await StartAsync(Client(host), token, "Maftuna", Png(400, 300)))).Should().Be("InvitationAccepted", "the invitation is spent");
        }
        finally
        {
            await PostgresTestServer.DropDatabaseAsync(connectionString);
        }
    }

    private static WebApiTestHost<Program> CreateHost(string connectionString, FakeTelegramBotClient bot)
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
                ["Identity:SignUp:Profile:Picture"] = "Required",
                ["Identity:Methods:Password:IsEnabled"] = "false",
                ["Identity:Methods:Email:IsEnabled"] = "false",
                ["Identity:Telegram:IsEnabled"] = "true",
                ["Identity:Telegram:BotToken"] = "123456:E2E-token",
                ["Identity:Telegram:BotUserName"] = "ocharo_e2e_bot",
                ["Identity:Telegram:WebhookSecret"] = Secret,
                ["Identity:Phone:AllowedCountries:0"] = "UZ",
                ["Identity:Otp:HashKey"] = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
                ["Identity:Otp:OriginRateLimitWindow"] = "00:00:00",
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

    private static HttpClient Client(WebApiTestHost<Program> host)
        => host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    /// <summary>Makes Ocharo with its owner, and an editor invitation from the host CLI's service; the organization and the token.</summary>
    private static async Task<(Guid OrganizationId, string Token)> InviteAsync(WebApiTestHost<Program> host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var owner = await scope.ServiceProvider.GetRequiredService<FirstOwnerService>().CreateAsync(new FirstOwnerRequest
        {
            Slug = "ocharo",
            OrganizationName = "Ocharo",
            DisplayName = "Sultonbek",
            PhoneNumber = "+998907777777",
        });
        var invited = await scope.ServiceProvider.GetRequiredService<AdminInvitationService>().CreateAsync(new AdminInvitationRequest { Organization = "ocharo", Role = "editor" });
        invited.Status.Should().Be(AdminInvitationStatus.Created, invited.Error);
        return (owner.OrganizationId!.Value, invited.Token!);
    }

    private static async Task<HttpResponseMessage> StartAsync(HttpClient client, string token, string? name, byte[] picture)
    {
        using var form = new MultipartFormDataContent();
        if (name is not null)
            form.Add(new StringContent(name), "displayName");
        var file = new ByteArrayContent(picture);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "picture", "me.png");
        return await client.PostAsync($"/api/account/invitations/{token}/sign-up?clientId=desk", form);
    }

    private static async Task<JsonElement> StatusAsync(HttpClient client) => await DataAsync(await client.GetAsync("/api/account/invitations/sign-up/status"));

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").Clone();
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        using (response)
        {
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return problem.RootElement.TryGetProperty("identityCode", out var code) ? code.GetString() : null;
        }
    }

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Teal);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static async Task<HttpResponseMessage> PostUpdateAsync(HttpClient client, string update)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/telegram/webhook") { Content = new StringContent(update, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", Secret);
        return await client.SendAsync(request);
    }

    private static string StartUpdate(string url) => Update($$"""
        "text": "/start {{url[(url.IndexOf('=') + 1)..]}}"
        """);

    private static string ContactUpdate(string phone) => Update($$"""
        "contact": { "phone_number": "{{phone}}", "first_name": "Maftuna", "user_id": {{ChatId}} }
        """);

    private static string Update(string body) => $$"""
        {
          "update_id": 1,
          "message": {
            "message_id": 1,
            "date": 1793520000,
            "chat": { "id": {{ChatId}}, "type": "private", "first_name": "Maftuna" },
            "from": { "id": {{ChatId}}, "is_bot": false, "first_name": "Maftuna", "language_code": "en" },
            {{body}}
          }
        }
        """;
}

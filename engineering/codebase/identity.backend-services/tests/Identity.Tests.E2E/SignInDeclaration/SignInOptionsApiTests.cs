using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AwesomeAssertions;
using Identity.Tests.E2E.PhoneSignIn;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Telegram.Bot;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Identity.Tests.E2E.SignInDeclaration;

/// <summary>
/// <c>GET /api/account/sign-in-options</c> through the real service: anonymous, cached briefly behind an <c>ETag</c>, the
/// methods the settings turn on (phone only while the bot is on), a registered app's own theme and background, the
/// instance's look for an unknown app, and the rule messages in the request's locale.
/// </summary>
[Collection(IdentityApiCollection.Name)]
public sealed class SignInOptionsApiTests(IdentityApiFixture fixture)
{
    private const string Route = "/api/account/sign-in-options";

    [Fact]
    public async Task Options_ShouldListPasswordOnly_AndCacheBehindAnETag_WhenTheBotAndSignUpAreOff()
    {
        using var client = fixture.Host.CreateClient();

        using var response = await client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.Public.Should().BeTrue();
        response.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromMinutes(1));
        response.Headers.Vary.Should().Contain("Accept-Language");
        var tag = response.Headers.ETag!;
        tag.IsWeak.Should().BeFalse();

        var data = await DataAsync(response);
        data.GetProperty("brandName").GetString().Should().Be("Identity");
        data.GetProperty("theme").GetString().Should().Be("platform");
        data.GetProperty("isSignUpEnabled").GetBoolean().Should().BeFalse();
        data.GetProperty("locale").GetString().Should().Be("en");
        Ids(data).Should().Equal("password");
        var password = data.GetProperty("methods")[0];
        password.GetProperty("fields").EnumerateArray().Select(field => field.GetProperty("name").GetString()).Should().Equal("login", "password");
        var minimum = password.GetProperty("passwordRules")[0];
        minimum.GetProperty("code").GetString().Should().Be("min");
        minimum.GetProperty("params").GetProperty("min").GetInt32().Should().Be(15);
        minimum.GetProperty("message").GetString().Should().Be("Passwords must be at least 15 characters.");

        using var again = new HttpRequestMessage(HttpMethod.Get, Route);
        again.Headers.IfNoneMatch.Add(tag);
        using var revalidated = await client.SendAsync(again);
        revalidated.StatusCode.Should().Be(HttpStatusCode.NotModified);
        (await revalidated.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Options_ShouldFollowTheSettings_AppsLook_AndLocale_WhenTheBotIsOn()
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync();
        try
        {
            await using var host = CreateHost(connectionString);
            using var client = host.CreateClient();

            using var instance = await client.GetAsync(Route);
            var data = await DataAsync(instance);
            data.GetProperty("brandName").GetString().Should().Be("Ocharo");
            data.GetProperty("theme").GetString().Should().Be("platform");
            data.GetProperty("backgroundUrl").GetString().Should().Be("/backgrounds/instance.jpg");
            data.GetProperty("isSignUpEnabled").GetBoolean().Should().BeTrue();
            data.GetProperty("requirements").GetProperty("isConfirmedPhoneNumberRequired").GetBoolean().Should().BeTrue();
            Ids(data).Should().Equal("password", "phone-code");
            var phone = data.GetProperty("methods")[1];
            phone.GetProperty("channels").EnumerateArray().Select(channel => channel.GetString()).Should().Equal("telegram");
            phone.GetProperty("fields")[0].GetProperty("countries").EnumerateArray().Select(country => country.GetString()).Should().Equal("UZ");

            using var app = await client.GetAsync($"{Route}?clientId=ocharo-web");
            var look = await DataAsync(app);
            look.GetProperty("theme").GetString().Should().Be("ocharo");
            look.GetProperty("backgroundUrl").GetString().Should().Be("/backgrounds/ocharo.jpg");
            app.Headers.ETag.Should().NotBe(instance.Headers.ETag);

            using var unknown = await client.GetAsync($"{Route}?clientId=not-registered");
            (await unknown.Content.ReadAsStringAsync()).Should().Be(await instance.Content.ReadAsStringAsync(), "an unknown app must not tell which apps exist");
            unknown.Headers.ETag.Should().Be(instance.Headers.ETag);

            using var russian = await client.GetAsync($"{Route}?locale=ru");
            var ru = await DataAsync(russian);
            ru.GetProperty("locale").GetString().Should().Be("ru");
            ru.GetProperty("methods")[0].GetProperty("passwordRules")[0].GetProperty("message").GetString().Should().Be("Пароль должен содержать не менее 15 символов.");
            russian.Headers.ETag.Should().NotBe(instance.Headers.ETag);

            using var browser = new HttpRequestMessage(HttpMethod.Get, Route);
            browser.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("de-DE"));
            browser.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("uz-UZ", 0.8));
            using var uzbek = await client.SendAsync(browser);
            var uz = await DataAsync(uzbek);
            uz.GetProperty("locale").GetString().Should().Be("uz");
            uz.GetProperty("methods")[0].GetProperty("passwordRules")[0].GetProperty("message").GetString().Should().Be("Parol kamida 15 ta belgidan iborat boʻlishi kerak.");
            uz.GetProperty("methods")[1].GetProperty("fields")[0].GetProperty("rules")[0].GetProperty("message").GetString()
                .Should().Be("Enter your phone number.", "the uz texts of the new keys are not written yet");
        }
        finally
        {
            await PostgresTestServer.DropDatabaseAsync(connectionString);
        }
    }

    private static WebApiTestHost<Program> CreateHost(string connectionString) => new()
    {
        ConfigureConfigurationHook = configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DatabaseSettings:ConnectionString"] = connectionString,
            ["AllowedHosts"] = "localhost",
            ["Logging:LogLevel:Default"] = "Warning",
            ["Identity:SignUp:IsEnabled"] = "true",
            ["Identity:SignIn:IsConfirmedPhoneNumberRequired"] = "true",
            ["Identity:Pages:BrandName"] = "Ocharo",
            ["Identity:Pages:BackgroundUrl"] = "/backgrounds/instance.jpg",
            ["Identity:Server:Clients:0:ClientId"] = "ocharo-web",
            ["Identity:Server:Clients:0:ClientSecret"] = "e2e-test-only-client-secret",
            ["Identity:Server:Clients:0:RedirectUris:0"] = "https://ocharo.example.test/signin-oidc",
            ["Identity:Server:Clients:0:Theme"] = "ocharo",
            ["Identity:Server:Clients:0:BackgroundUrl"] = "/backgrounds/ocharo.jpg",
            ["Identity:Telegram:IsEnabled"] = "true",
            ["Identity:Telegram:BotToken"] = "123456:E2E-token",
            ["Identity:Telegram:BotUserName"] = "ocharo_e2e_bot",
            ["Identity:Telegram:WebhookSecret"] = "hook-secret_e2e",
            ["Identity:Phone:AllowedCountries:0"] = "UZ",
            ["Identity:Otp:HashKey"] = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
        }),
        ConfigureServicesHook = services =>
        {
            services.RemoveAll<ITelegramBotClient>();
            services.AddSingleton<ITelegramBotClient>(new FakeTelegramBotClient());
        },
    };

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("data").Clone();
    }

    private static IEnumerable<string?> Ids(JsonElement data)
        => data.GetProperty("methods").EnumerateArray().Select(method => method.GetProperty("id").GetString());
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using AwesomeAssertions;
using Identity.Tests.E2E.Accounts;

namespace Identity.Tests.E2E.TestMode;

/// <summary>
/// Test mode on the real service: with the switch on and no bot settings at all, a phone signs in through a fake Telegram and
/// every code and link the instance sends, email and Telegram, is readable at <c>GET /api/test/messages?to=</c>; with it off
/// the route answers 404 and nothing is kept.
/// </summary>
public sealed partial class TestModeApiTests
{
    private const string Phone = "+998901234567";
    private const string Email = "zoe@example.test";

    [Fact]
    public async Task TestMode_ShouldSignInByPhone_WithNoBotSettings_AndKeepTheCodeInTheSink()
    {
        await using var host = new AccountHost(OnSettings(), keepRealBroker: true);
        using var client = host.CreateClient();

        using var sent = await client.PostAsJsonAsync("/api/account/phone-sign-in/send", new { phone = Phone });
        var data = await AccountHost.DataAsync(sent);
        data.GetProperty("isCodeSent").GetBoolean().Should().BeTrue("the fake channel completed the link");
        data.GetProperty("telegramLinkUrl").ValueKind.Should().Be(JsonValueKind.Null);

        var messages = await MessagesAsync(client, Phone);
        messages.Should().NotBeEmpty();
        messages.Should().OnlyContain(m => m.GetProperty("channel").GetString() == "telegram");
        var code = CodePattern().Match(messages[^1].GetProperty("text").GetString()!).Value;
        code.Should().HaveLength(6);

        using var wrong = await client.PostAsJsonAsync("/api/account/phone-sign-in?useCookies=true", new { phone = Phone, code = code == "000000" ? "111111" : "000000" });
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var signedIn = await client.PostAsJsonAsync("/api/account/phone-sign-in?useCookies=true", new { phone = Phone, code });
        signedIn.StatusCode.Should().Be(HttpStatusCode.NoContent);
        signedIn.Headers.GetValues("Set-Cookie").Should().Contain(cookie => cookie.StartsWith("__Host-identity="));
        (await client.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await MessagesAsync(client, "+998907654321")).Should().BeEmpty("another number has no messages");
    }

    [Fact]
    public async Task TestMode_ShouldKeepAccountEmails_AndSendAPasswordChangeCodeToTelegram_ForAPhoneOnlyPerson()
    {
        var settings = OnSettings();
        settings["Identity:Password:Change:Confirmation"] = "Code";
        await using var host = new AccountHost(settings, keepRealBroker: true);
        using var client = host.CreateClient();

        using var registered = await client.PostAsJsonAsync("/api/account/register", new { email = Email, password = AccountHost.StrongPassword });
        (await AccountHost.DataAsync(registered)).GetProperty("isConfirmationSent").GetBoolean().Should().BeTrue("test mode accepts the email");
        var mail = (await MessagesAsync(client, Email)).Should().ContainSingle().Which;
        mail.GetProperty("channel").GetString().Should().Be("email");
        mail.GetProperty("subject").GetString().Should().Be("Confirm your email");
        var link = new Uri(UrlPattern().Match(mail.GetProperty("text").GetString()!).Value);
        link.GetLeftPart(UriPartial.Path).Should().Be("https://id.example.test/confirm-email");
        HttpUtility.ParseQueryString(link.Query)["token"].Should().NotBeNullOrEmpty();

        using var phoneClient = host.CreateClient();
        await phoneClient.PostAsJsonAsync("/api/account/phone-sign-in/send", new { phone = Phone });
        var code = CodePattern().Match((await MessagesAsync(phoneClient, Phone))[^1].GetProperty("text").GetString()!).Value;
        (await phoneClient.PostAsJsonAsync("/api/account/phone-sign-in?useCookies=true", new { phone = Phone, code })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var before = (await MessagesAsync(phoneClient, Phone)).Count;

        var confirmation = await AccountHost.DataAsync(await phoneClient.PostAsJsonAsync("/api/account/manage/password/confirmation", new { }));

        confirmation.GetProperty("isSent").GetBoolean().Should().BeTrue("Telegram is the person's verified channel");
        var after = await MessagesAsync(phoneClient, Phone);
        after.Should().HaveCount(before + 1);
        CodePattern().IsMatch(after[^1].GetProperty("text").GetString()!).Should().BeTrue();
        (await MessagesAsync(phoneClient, null)).Should().HaveCountGreaterThan(after.Count - 1, "no filter returns every kept message");
    }

    [Fact]
    public async Task TestMode_ShouldAnswer404AndKeepNothing_WhileOff()
    {
        await using var off = new AccountHost(new Dictionary<string, string?> { ["Identity:TestMode:IsEnabled"] = "false" });
        await using var unset = new AccountHost();

        foreach (var host in new[] { off, unset })
        {
            using var client = host.CreateClient();
            (await client.GetAsync("/api/test/messages")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.GetAsync("/api/test/messages?to=" + Uri.EscapeDataString(Phone))).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.PostAsJsonAsync("/api/account/phone-sign-in/send", new { phone = Phone })).StatusCode.Should().Be(HttpStatusCode.NotFound, "the bot is off too");
        }
    }

    private static Dictionary<string, string?> OnSettings() => new()
    {
        ["Identity:TestMode:IsEnabled"] = "true",
        ["Identity:Phone:AllowedCountries:0"] = "UZ",
        ["Identity:Otp:HashKey"] = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
    };

    /// <summary>The kept messages, optionally for one recipient.</summary>
    private static async Task<IReadOnlyList<JsonElement>> MessagesAsync(HttpClient client, string? to)
    {
        using var response = await client.GetAsync("/api/test/messages" + (to is null ? string.Empty : "?to=" + Uri.EscapeDataString(to)));
        return [.. (await AccountHost.DataAsync(response)).EnumerateArray().Select(message => message.Clone())];
    }

    [GeneratedRegex("[0-9]{6}")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"https://\S+")]
    private static partial Regex UrlPattern();
}

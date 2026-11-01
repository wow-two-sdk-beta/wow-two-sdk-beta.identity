using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;

namespace Identity.Tests.E2E.Accounts;

/// <summary>
/// A stricter password rule is met at the next sign-in (C1b): once the typed password matches its hash it is checked against the
/// current rules, a failing one signs in to the change form only, and the old hash stays until the person changes it.
/// </summary>
public sealed class RequiredPasswordChangeTests
{
    private const string Email = "bea@example.test";

    /// <summary>13 characters: it met a minimum of 8 and breaks the default 15.</summary>
    private const string OldPassword = "old-password!";
    private const string NewPassword = "a brand new passphrase";

    private static readonly Dictionary<string, string?> LooseRules = new() { ["Identity:Password:MinLength"] = "8" };

    [Fact]
    public async Task SignIn_ShouldAnswerThatAChangeIsRequired_AndGrantNoSession_WhenThePasswordBreaksTheCurrentRules()
    {
        await using var before = new AccountHost(LooseRules);
        var id = await before.CreateUserAsync(Email, OldPassword);
        var hash = (await before.FindAsync(id)).PasswordHash;
        await using var host = new AccountHost(connectionString: before.ConnectionString);
        using var client = host.CreateClient();

        using var response = await AccountHost.SignInAsync(client, Email, OldPassword);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Set-Cookie").Should().BeFalse("no session starts");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var data = await AccountHost.DataAsync(response);
        data.GetProperty("isPasswordChangeRequired").GetBoolean().Should().BeTrue();
        data.GetProperty("confirmation").GetString().Should().Be("None");
        data.GetProperty("ticket").GetString().Should().NotBeNullOrWhiteSpace();
        (await client.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await host.FindAsync(id)).PasswordHash.Should().Be(hash, "the old hash stays until the person changes it");
        await using var scope = host.Host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<SignInLogService>().ListForUserAsync(id)).Should()
            .ContainSingle(entry => entry.Result == SignInResultConstants.PasswordChangeRequired && entry.Reason == SignInReasonConstants.PasswordBreaksRules);
    }

    [Fact]
    public async Task SignIn_ShouldStayIgnorantOfTheRules_WhenThePasswordIsWrong()
    {
        await using var before = new AccountHost(LooseRules);
        await before.CreateUserAsync(Email, OldPassword);
        await using var host = new AccountHost(connectionString: before.ConnectionString);
        using var client = host.CreateClient();

        using var response = await AccountHost.SignInAsync(client, Email, "wrong");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SignIn_ShouldSucceedAsBefore_WhenThePasswordMeetsTheCurrentRules()
    {
        await using var host = new AccountHost();
        await host.CreateUserAsync(Email);
        using var client = host.CreateClient();

        using var response = await AccountHost.SignInAsync(client, Email, AccountHost.StrongPassword);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ticket_ShouldChangeThePassword_AndOpenNothingElse_UnderNone()
    {
        await using var before = new AccountHost(LooseRules);
        await before.CreateUserAsync(Email, OldPassword);
        await using var host = new AccountHost(connectionString: before.ConnectionString);
        using var client = host.CreateClient();
        var ticket = (await AccountHost.DataAsync(await AccountHost.SignInAsync(client, Email, OldPassword))).GetProperty("ticket").GetString();

        // The ticket is no bearer token and no session: it opens the change routes only.
        using var asBearer = new HttpRequestMessage(HttpMethod.Get, "/api/account/manage/info");
        asBearer.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ticket);
        (await client.SendAsync(asBearer)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/account/manage/password", new { newPassword = "short", ticket })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "the new password still meets the rules");

        using var changed = await client.PostAsJsonAsync("/api/account/manage/password", new { newPassword = NewPassword, ticket });

        changed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the change signs no one in");
        (await client.PostAsJsonAsync("/api/account/manage/password", new { newPassword = "yet another passphrase", ticket })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "the change spends the ticket");
        (await AccountHost.SignInAsync(client, Email, OldPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AccountHost.SignInAsync(client, Email, NewPassword)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Ticket_ShouldBeRefused_WhenItIsForgedOrBelongsToAnotherPerson()
    {
        await using var before = new AccountHost(LooseRules);
        await before.CreateUserAsync(Email, OldPassword);
        var other = await before.CreateUserAsync("cal@example.test", OldPassword);
        await using var host = new AccountHost(connectionString: before.ConnectionString);
        using var client = host.CreateClient();
        var ticket = (await AccountHost.DataAsync(await AccountHost.SignInAsync(client, Email, OldPassword))).GetProperty("ticket").GetString()!;
        var secret = ticket[(ticket.IndexOf('.', StringComparison.Ordinal) + 1)..];

        (await client.PostAsJsonAsync("/api/account/manage/password", new { newPassword = NewPassword, ticket = ticket[..^2] + "xx" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/account/manage/password", new { newPassword = NewPassword, ticket = $"{other:N}.{secret}" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/account/manage/password", new { newPassword = NewPassword, ticket = secret })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ticket_ShouldExpireAfterFifteenMinutes()
    {
        await using var before = new AccountHost(LooseRules);
        await before.CreateUserAsync(Email, OldPassword);
        await using var host = new AccountHost(connectionString: before.ConnectionString);
        using var client = host.CreateClient();
        var ticket = (await AccountHost.DataAsync(await AccountHost.SignInAsync(client, Email, OldPassword))).GetProperty("ticket").GetString();

        host.Host.Clock.Advance(TimeSpan.FromMinutes(16));

        (await client.PostAsJsonAsync("/api/account/manage/password", new { newPassword = NewPassword, ticket })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ticket_ShouldFollowTheConfirmationSetting_UnderCode()
    {
        await using var before = new AccountHost(LooseRules);
        await before.CreateUserAsync(Email, OldPassword);
        await using var host = new AccountHost(
            new Dictionary<string, string?> { ["Identity:Password:Change:Confirmation"] = "Code" },
            before.ConnectionString);
        using var client = host.CreateClient();
        var required = await AccountHost.DataAsync(await AccountHost.SignInAsync(client, Email, OldPassword));
        required.GetProperty("confirmation").GetString().Should().Be("Code");
        var ticket = required.GetProperty("ticket").GetString();

        var sent = await AccountHost.DataAsync(await client.PostAsJsonAsync("/api/account/manage/password/confirmation", new { ticket }));
        sent.GetProperty("isSent").GetBoolean().Should().BeTrue();
        var code = Regex.Match(host.Mail.Sent.Single().TextBody!, @"\b\d{6}\b").Value;

        (await client.PostAsJsonAsync("/api/account/manage/password", new { newPassword = NewPassword, ticket })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "the change needs the code too");
        (await client.PostAsJsonAsync("/api/account/manage/password", new { newPassword = NewPassword, ticket, code })).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await AccountHost.SignInAsync(client, Email, NewPassword)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Ticket_ShouldReachTheLinkPage_UnderLink()
    {
        await using var before = new AccountHost(LooseRules);
        await before.CreateUserAsync(Email, OldPassword);
        await using var host = new AccountHost(
            new Dictionary<string, string?> { ["Identity:Password:Change:Confirmation"] = "Link" },
            before.ConnectionString);
        using var client = host.CreateClient();
        var ticket = (await AccountHost.DataAsync(await AccountHost.SignInAsync(client, Email, OldPassword))).GetProperty("ticket").GetString();

        (await client.PostAsJsonAsync("/api/account/manage/password", new { newPassword = NewPassword, ticket })).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "under Link only the emailed link changes a password");
        (await AccountHost.DataAsync(await client.PostAsJsonAsync("/api/account/manage/password/confirmation", new { ticket }))).GetProperty("isSent").GetBoolean().Should().BeTrue();
        var token = Uri.UnescapeDataString(host.Mail.LinkOf("Change your password", Email).Fragment.Replace("#token=", string.Empty, StringComparison.Ordinal));

        (await client.PostAsJsonAsync("/api/account/password/change", new { token, newPassword = NewPassword })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await AccountHost.SignInAsync(client, Email, NewPassword)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}

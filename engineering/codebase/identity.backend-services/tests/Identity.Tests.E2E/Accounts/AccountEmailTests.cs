using System.Net;
using System.Net.Http.Json;
using System.Web;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Comms.Email;

namespace Identity.Tests.E2E.Accounts;

/// <summary>
/// The account emails as a deployed instance sends them: the confirmation and reset links point at the hosted pages on
/// the issuer's origin, an instance with no SMTP relay still boots and skips them, and a host that cannot issue bearer
/// tokens answers a bearer sign-in with a 400 problem.
/// </summary>
public sealed class AccountEmailTests
{
    [Fact]
    public async Task Register_ShouldBootAndSendNothing_WhenNoSmtpRelayIsConfigured()
    {
        await using var host = new AccountHost(keepRealBroker: true);
        using var client = host.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/account/register", new { email = "quiet@example.test", password = AccountHost.StrongPassword });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await AccountHost.DataAsync(response)).GetProperty("isConfirmationSent").GetBoolean().Should().BeFalse();
        var skipped = await host.Host.Services.GetRequiredService<IEmailBroker>().SendAsync(EmailMessage.Create("a@example.test", "Subject", "Body"));
        skipped.Success.Should().BeFalse();
        skipped.FailureReason.Should().Be("smtp_not_configured");
    }

    [Fact]
    public async Task Register_ShouldMailAConfirmationLinkToTheHostedPage_ThatConfirmsTheEmail()
    {
        await using var host = new AccountHost();
        using var client = host.CreateClient();

        using var registered = await client.PostAsJsonAsync("/api/account/register", new { email = "zoe@example.test", password = AccountHost.StrongPassword });

        var data = await AccountHost.DataAsync(registered);
        data.GetProperty("isConfirmationSent").GetBoolean().Should().BeTrue();
        var link = host.Mail.LinkOf("Confirm your email", "zoe@example.test");
        link.GetLeftPart(UriPartial.Path).Should().Be("https://id.example.test/confirm-email");
        var query = HttpUtility.ParseQueryString(link.Query);
        query["userId"].Should().Be(data.GetProperty("userId").GetString());

        // The hosted page calls the account API with the link's own parameters.
        using var confirmed = await client.GetAsync("/api/account/confirm-email" + link.Query);
        confirmed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await host.FindAsync(Guid.Parse(query["userId"]!))).IsEmailConfirmed.Should().BeTrue();

        using var spent = await client.GetAsync("/api/account/confirm-email?userId=" + query["userId"] + "&token=wrong");
        spent.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ForgotPassword_ShouldMailAResetLinkToTheHostedPage_ThatSetsANewPassword()
    {
        await using var host = new AccountHost();
        await host.CreateUserAsync("ned@example.test");
        using var client = host.CreateClient();

        (await client.PostAsJsonAsync("/api/account/forgot-password", new { email = "ned@example.test" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync("/api/account/forgot-password", new { email = "nobody@example.test" })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        host.Mail.Sent.Should().ContainSingle("only the known address gets a reset email");
        var link = host.Mail.LinkOf("Reset your password", "ned@example.test");
        link.GetLeftPart(UriPartial.Path).Should().Be("https://id.example.test/reset-password");
        var query = HttpUtility.ParseQueryString(link.Query);
        query["email"].Should().Be("ned@example.test");

        const string replacement = "a brand new passphrase";
        using var reset = await client.PostAsJsonAsync("/api/account/reset-password", new { email = query["email"], resetToken = query["token"], newPassword = replacement });
        reset.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await AccountHost.SignInAsync(client, "ned@example.test", AccountHost.StrongPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AccountHost.SignInAsync(client, "ned@example.test", replacement)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Register_ShouldRefuseAPasswordBelowTheDefaultMinimumOf15()
    {
        await using var host = new AccountHost();
        using var client = host.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/account/register", new { email = "tina@example.test", password = "fourteen chars" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AccountHost.ProblemAsync(response)).GetProperty("errors")[0].GetProperty("code").GetString().Should().Be("PasswordTooShort");
    }

    [Fact]
    public async Task Login_ShouldAnswer400WithABearerCode_NotA500_OnACookieOnlyInstance()
    {
        await using var host = new AccountHost();
        await host.CreateUserAsync("uma@example.test");
        using var client = host.CreateClient();

        using var bearer = await client.PostAsJsonAsync("/api/account/login", new { login = "uma@example.test", password = AccountHost.StrongPassword });
        using var wrong = await client.PostAsJsonAsync("/api/account/login", new { login = "uma@example.test", password = "not the password" });
        using var refresh = await client.PostAsJsonAsync("/api/account/refresh", new { refreshToken = "anything" });

        foreach (var response in new[] { bearer, wrong, refresh })
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var problem = await AccountHost.ProblemAsync(response);
            problem.GetProperty("identityCode").GetString().Should().Be("BearerSignInUnsupported");
            problem.GetProperty("detail").GetString().Should().Contain("useCookies=true");
        }

        (await AccountHost.SignInAsync(client, "uma@example.test", AccountHost.StrongPassword)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}

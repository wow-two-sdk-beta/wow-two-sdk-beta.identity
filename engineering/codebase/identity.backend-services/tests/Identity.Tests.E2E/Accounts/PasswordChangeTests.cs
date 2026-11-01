using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace Identity.Tests.E2E.Accounts;

/// <summary>
/// The password change as an instance runs it, per <c>Identity:Password:Change:Confirmation</c>: refused without the proof
/// the setting names, done with it, and every change ends the person's other sessions (and this one only when
/// <c>IsSignInAgainRequired</c> says so).
/// </summary>
public sealed partial class PasswordChangeTests
{
    private const string Email = "ann@example.test";
    private const string Replacement = "a brand new passphrase";

    [Fact]
    public async Task None_ShouldChangeThePassword_KeepThisSessionAndEndTheOthers()
    {
        await using var host = new AccountHost();
        await host.CreateUserAsync(Email);
        using var here = host.CreateClient();
        using var elsewhere = host.CreateClient();
        (await AccountHost.SignInAsync(here, Email, AccountHost.StrongPassword)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await AccountHost.SignInAsync(elsewhere, Email, AccountHost.StrongPassword)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var confirmation = await AccountHost.DataAsync(await here.PostAsJsonAsync("/api/account/manage/password/confirmation", new { }));
        confirmation.GetProperty("confirmation").GetString().Should().Be("None");
        confirmation.GetProperty("isSent").GetBoolean().Should().BeFalse();

        using var changed = await here.PostAsJsonAsync("/api/account/manage/password", new { currentPassword = AccountHost.StrongPassword, newPassword = Replacement });

        changed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        host.Mail.Sent.Should().BeEmpty("nothing confirms a change under None");
        (await here.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.OK, "this session stays signed in");
        (await elsewhere.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "every other session ends");
        using var fresh = host.CreateClient();
        (await AccountHost.SignInAsync(fresh, Email, AccountHost.StrongPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AccountHost.SignInAsync(fresh, Email, Replacement)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The kept session still lists, and the cookie it holds keeps working after the next request too.
        (await here.GetAsync("/api/account/manage/sessions")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await here.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task None_ShouldRefuseAWrongCurrentPasswordAndAWeakNewOne_AndChangeNothing()
    {
        await using var host = new AccountHost();
        await host.CreateUserAsync(Email);
        using var client = host.CreateClient();
        await AccountHost.SignInAsync(client, Email, AccountHost.StrongPassword);

        using var wrong = await client.PostAsJsonAsync("/api/account/manage/password", new { currentPassword = "not the password", newPassword = Replacement });
        using var weak = await client.PostAsJsonAsync("/api/account/manage/password", new { currentPassword = AccountHost.StrongPassword, newPassword = "short" });
        using var absent = await client.PostAsJsonAsync("/api/account/manage/password", new { newPassword = Replacement });

        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var mismatch = (await AccountHost.ProblemAsync(wrong)).GetProperty("errors")[0];
        mismatch.GetProperty("code").GetString().Should().Be("PasswordMismatch");
        mismatch.GetProperty("property").GetString().Should().Be("currentPassword");
        weak.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AccountHost.ProblemAsync(weak)).GetProperty("errors")[0].GetProperty("code").GetString().Should().Be("PasswordTooShort");
        absent.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var again = host.CreateClient();
        (await AccountHost.SignInAsync(again, Email, AccountHost.StrongPassword)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Change_ShouldAnswer401_WhenNoOneIsSignedInAndNoTicketIsGiven()
    {
        await using var host = new AccountHost();
        await host.CreateUserAsync(Email);
        using var client = host.CreateClient();

        (await client.PostAsJsonAsync("/api/account/manage/password/confirmation", new { })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/account/manage/password", new { currentPassword = AccountHost.StrongPassword, newPassword = Replacement })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/account/manage/password", new { newPassword = Replacement, ticket = "0123456789abcdef0123456789abcdef.forged" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IsSignInAgainRequired_ShouldEndThisSessionToo()
    {
        await using var host = new AccountHost(new Dictionary<string, string?> { ["Identity:Password:Change:IsSignInAgainRequired"] = "true" });
        await host.CreateUserAsync(Email);
        using var here = host.CreateClient();
        await AccountHost.SignInAsync(here, Email, AccountHost.StrongPassword);

        using var changed = await here.PostAsJsonAsync("/api/account/manage/password", new { currentPassword = AccountHost.StrongPassword, newPassword = Replacement });

        changed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await here.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AccountHost.SignInAsync(here, Email, Replacement)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Code_ShouldChangeThePassword_OnlyWithTheCodeSentToTheVerifiedEmail()
    {
        await using var host = new AccountHost(new Dictionary<string, string?> { ["Identity:Password:Change:Confirmation"] = "Code" });
        await host.CreateUserAsync(Email);
        using var client = host.CreateClient();
        await AccountHost.SignInAsync(client, Email, AccountHost.StrongPassword);

        var confirmation = await AccountHost.DataAsync(await client.PostAsJsonAsync("/api/account/manage/password/confirmation", new { }));
        confirmation.GetProperty("confirmation").GetString().Should().Be("Code");
        confirmation.GetProperty("isSent").GetBoolean().Should().BeTrue();
        var sent = host.Mail.Sent.Should().ContainSingle().Subject;
        sent.To[0].Address.Should().Be(Email);
        var code = Regex.Match(sent.TextBody!, @"\b\d{6}\b").Value;
        code.Should().NotBeEmpty();
        sent.Subject.Should().Be("Your Identity verification code", "the instance names itself in the message");

        using var withoutCode = await client.PostAsJsonAsync("/api/account/manage/password", new { currentPassword = AccountHost.StrongPassword, newPassword = Replacement });
        using var wrongCode = await client.PostAsJsonAsync("/api/account/manage/password", new { currentPassword = AccountHost.StrongPassword, newPassword = Replacement, code = code == "000000" ? "111111" : "000000" });
        using var weakPassword = await client.PostAsJsonAsync("/api/account/manage/password", new { currentPassword = AccountHost.StrongPassword, newPassword = "short", code });

        withoutCode.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AccountHost.ProblemAsync(withoutCode)).GetProperty("errors")[0].GetProperty("code").GetString().Should().Be("InvalidPasswordChangeCode");
        wrongCode.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        weakPassword.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the rules run before the code is spent");

        using var changed = await client.PostAsJsonAsync("/api/account/manage/password", new { currentPassword = AccountHost.StrongPassword, newPassword = Replacement, code });
        changed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var replayed = await client.PostAsJsonAsync("/api/account/manage/password", new { currentPassword = Replacement, newPassword = "yet another passphrase", code });
        replayed.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a code works once");
        using var fresh = host.CreateClient();
        (await AccountHost.SignInAsync(fresh, Email, Replacement)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Code_ShouldExpireAfterFifteenMinutes()
    {
        await using var host = new AccountHost(new Dictionary<string, string?> { ["Identity:Password:Change:Confirmation"] = "Code" });
        await host.CreateUserAsync(Email);
        using var client = host.CreateClient();
        await AccountHost.SignInAsync(client, Email, AccountHost.StrongPassword);
        await client.PostAsJsonAsync("/api/account/manage/password/confirmation", new { });
        var code = Regex.Match(host.Mail.Sent.Single().TextBody!, @"\b\d{6}\b").Value;

        host.Host.Clock.Advance(TimeSpan.FromMinutes(16));

        using var late = await client.PostAsJsonAsync("/api/account/manage/password", new { currentPassword = AccountHost.StrongPassword, newPassword = Replacement, code });
        late.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AccountHost.ProblemAsync(late)).GetProperty("errors")[0].GetProperty("code").GetString().Should().Be("InvalidPasswordChangeCode");
    }

    [Fact]
    public async Task Link_ShouldChangeThePassword_OnlyThroughTheLinkInTheEmail()
    {
        await using var host = new AccountHost(new Dictionary<string, string?> { ["Identity:Password:Change:Confirmation"] = "Link" });
        await host.CreateUserAsync(Email);
        using var here = host.CreateClient();
        using var anonymous = host.CreateClient();
        await AccountHost.SignInAsync(here, Email, AccountHost.StrongPassword);

        var confirmation = await AccountHost.DataAsync(await here.PostAsJsonAsync("/api/account/manage/password/confirmation", new { }));
        confirmation.GetProperty("confirmation").GetString().Should().Be("Link");
        confirmation.GetProperty("isSent").GetBoolean().Should().BeTrue();
        var link = host.Mail.LinkOf("Change your password", Email);
        link.GetLeftPart(UriPartial.Path).Should().Be("https://id.example.test/change-password");
        link.Query.Should().BeEmpty("the token rides in the fragment, which no server or mail scanner sees");
        var token = Uri.UnescapeDataString(link.Fragment.Replace("#token=", string.Empty, StringComparison.Ordinal));

        using var signedInForm = await here.PostAsJsonAsync("/api/account/manage/password", new { currentPassword = AccountHost.StrongPassword, newPassword = Replacement });
        signedInForm.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AccountHost.ProblemAsync(signedInForm)).GetProperty("identityCode").GetString().Should().Be("PasswordChangeLinkRequired");
        using var forged = await anonymous.PostAsJsonAsync("/api/account/password/change", new { token = token[..^3] + "xyz", newPassword = Replacement });
        forged.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var weak = await anonymous.PostAsJsonAsync("/api/account/password/change", new { token, newPassword = "short" });
        weak.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the rules run before the link is spent");

        using var changed = await anonymous.PostAsJsonAsync("/api/account/password/change", new { token, newPassword = Replacement });

        changed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await here.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a change by link ends every session");
        using var replayed = await anonymous.PostAsJsonAsync("/api/account/password/change", new { token, newPassword = "yet another passphrase" });
        replayed.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a link works once");
        (await AccountHost.ProblemAsync(replayed)).GetProperty("errors")[0].GetProperty("code").GetString().Should().Be("InvalidPasswordChangeLink");
        (await AccountHost.SignInAsync(anonymous, Email, Replacement)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Link_ShouldExpireAfterFifteenMinutes()
    {
        await using var host = new AccountHost(new Dictionary<string, string?> { ["Identity:Password:Change:Confirmation"] = "Link" });
        await host.CreateUserAsync(Email);
        using var client = host.CreateClient();
        await AccountHost.SignInAsync(client, Email, AccountHost.StrongPassword);
        await client.PostAsJsonAsync("/api/account/manage/password/confirmation", new { });
        var token = Uri.UnescapeDataString(host.Mail.LinkOf("Change your password", Email).Fragment.Replace("#token=", string.Empty, StringComparison.Ordinal));

        host.Host.Clock.Advance(TimeSpan.FromMinutes(16));

        using var late = await client.PostAsJsonAsync("/api/account/password/change", new { token, newPassword = Replacement });
        late.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LinkRoute_ShouldRefuseEveryToken_WhenTheInstanceDoesNotConfirmByLink()
    {
        await using var host = new AccountHost(new Dictionary<string, string?> { ["Identity:Password:Change:Confirmation"] = "Code" });
        var id = await host.CreateUserAsync(Email);
        using var client = host.CreateClient();
        await AccountHost.SignInAsync(client, Email, AccountHost.StrongPassword);
        await client.PostAsJsonAsync("/api/account/manage/password/confirmation", new { });
        var code = Regex.Match(host.Mail.Sent.Single().TextBody!, @"\b\d{6}\b").Value;

        using var response = await client.PostAsJsonAsync("/api/account/password/change", new { token = $"{id:N}.{code}", newPassword = Replacement });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var fresh = host.CreateClient();
        (await AccountHost.SignInAsync(fresh, Email, AccountHost.StrongPassword)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Confirmation_ShouldAnswerAProblem_WhenThePersonHasNoVerifiedChannel()
    {
        await using var host = new AccountHost(new Dictionary<string, string?> { ["Identity:Password:Change:Confirmation"] = "Code" });
        await host.CreateUserAsync(Email, isEmailConfirmed: false);
        using var client = host.CreateClient();
        await AccountHost.SignInAsync(client, Email, AccountHost.StrongPassword);

        using var response = await client.PostAsJsonAsync("/api/account/manage/password/confirmation", new { });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await AccountHost.ProblemAsync(response)).GetProperty("identityCode").GetString().Should().Be("NoVerifiedChannel");
        host.Mail.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Confirmation_ShouldAnswer429_WhenACodeWasSentMomentsAgo()
    {
        await using var host = new AccountHost(new Dictionary<string, string?>
        {
            ["Identity:Password:Change:Confirmation"] = "Code",
            ["Identity:Otp:RateLimitWindow"] = "00:00:30",
        });
        await host.CreateUserAsync(Email);
        using var client = host.CreateClient();
        await AccountHost.SignInAsync(client, Email, AccountHost.StrongPassword);

        (await client.PostAsJsonAsync("/api/account/manage/password/confirmation", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        using var second = await client.PostAsJsonAsync("/api/account/manage/password/confirmation", new { });

        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await AccountHost.ProblemAsync(second)).GetProperty("identityCode").GetString().Should().Be("PasswordChangeRateLimited");
        host.Mail.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task Link_ShouldFailToStart_WhenNoPageAddressCanBeDerived()
    {
        // No issuer, so no origin: a link template is required beside Link, and the start refuses without one.
        var act = () => new AccountHost(new Dictionary<string, string?>
        {
            ["Identity:Server:Issuer"] = null,
            ["Identity:Password:Change:Confirmation"] = "Link",
        });

        act.Should().Throw<Exception>().Which.ToString().Should().Contain("LinkTemplate");
        await Task.CompletedTask;
    }
}

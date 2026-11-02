using System.Net;
using System.Net.Http.Json;
using System.Web;
using AwesomeAssertions;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

namespace Identity.Tests.E2E.Accounts;

/// <summary>
/// The account API's guards as the service runs them: the credential routes' per-address rate limit, the emailed links
/// carrying a local return URL only, sign-up refusing what the store or a mail library cannot take, and the refused
/// precondition named in the 403's <c>identityCode</c>.
/// </summary>
public sealed class AccountHardeningTests
{
    [Fact]
    public async Task CredentialRoutes_ShouldAnswer429_PastTheirPerAddressPolicy()
    {
        await using var host = new AccountHost();
        using var client = host.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt <= AccountRateLimitPolicyConstants.DefaultPermitLimit; attempt++)
        {
            using var response = await client.PostAsJsonAsync("/api/account/resend-confirmation-email", new { email = "nobody@example.test" });
            statuses.Add(response.StatusCode);
        }

        statuses.Take(AccountRateLimitPolicyConstants.DefaultPermitLimit).Should().AllBeEquivalentTo(HttpStatusCode.NoContent);
        statuses[^1].Should().Be(HttpStatusCode.TooManyRequests, "one address gets 30 credential requests a minute by default");
    }

    [Fact]
    public async Task CredentialPolicy_ShouldTakeTheHostsRateLimitsSection_AndCoverEverySignInRoute()
    {
        await using var host = new AccountHost(new Dictionary<string, string?> { ["RateLimits:Policies:identity-credentials:PermitLimit"] = "2" });
        await host.CreateUserAsync("pia@example.test");
        using var client = host.CreateClient();

        (await AccountHost.SignInAsync(client, "pia@example.test", "wrong")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AccountHost.SignInAsync(client, "pia@example.test", "wrong")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AccountHost.SignInAsync(client, "pia@example.test", AccountHost.StrongPassword)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var limited = host.Host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == AccountRateLimitPolicyConstants.Credentials)
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToList();
        limited.Should().Contain(
        [
            "/api/account/sign-in", "/api/account/sign-up", "/api/account/forgot-password", "/api/account/reset-password",
            "/api/account/resend-confirmation-email", "/api/account/phone-sign-in", "/api/account/phone-sign-in/send",
        ]);
    }

    [Fact]
    public async Task EmailedLinks_ShouldCarryALocalReturnUrl_AndDropAForeignOne()
    {
        await using var host = new AccountHost();
        using var client = host.CreateClient();
        const string returnUrl = "/connect/authorize?client_id=ocharo-web&scope=openid";

        (await client.PostAsJsonAsync("/api/account/sign-up", new { email = "ava@example.test", password = AccountHost.StrongPassword, returnUrl })).EnsureSuccessStatusCode();
        ReturnUrlOf(host.Mail.LinkOf("Confirm your email", "ava@example.test")).Should().Be(returnUrl);

        (await client.PostAsJsonAsync("/api/account/resend-confirmation-email", new { email = "ava@example.test", returnUrl = "//evil.example/steal" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        ReturnUrlOf(host.Mail.LinkOf("Confirm your email", "ava@example.test")).Should().BeNull("a protocol-relative URL leaves the instance");

        (await client.PostAsJsonAsync("/api/account/forgot-password", new { email = "ava@example.test", returnUrl })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var reset = host.Mail.LinkOf("Reset your password", "ava@example.test");
        ReturnUrlOf(reset).Should().Be(returnUrl);
        HttpUtility.ParseQueryString(reset.Query)["token"].Should().NotBeNullOrEmpty("the token stays in the link beside it");

        (await client.PostAsJsonAsync("/api/account/forgot-password", new { email = "ava@example.test", returnUrl = "https://evil.example/" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        ReturnUrlOf(host.Mail.LinkOf("Reset your password", "ava@example.test")).Should().BeNull("only a local path rides along");
    }

    [Theory]
    [InlineData("a b@c.com", null, "email")]
    [InlineData("Name <named@example.test>", null, "email")]
    [InlineData("long@example.test", 257, "userName")]
    public async Task SignUp_ShouldAnswer400_AndKeepNoAccount_ForAnAddressOrNameTheStoreCannotTake(string email, int? userNameLength, string field)
    {
        await using var host = new AccountHost();
        using var client = host.CreateClient();
        var userName = userNameLength is { } length ? new string('u', length) : null;

        using var response = await client.PostAsJsonAsync("/api/account/sign-up", new { email, password = AccountHost.StrongPassword, userName });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AccountHost.ProblemAsync(response)).GetProperty("errors")[0].GetProperty("property").GetString().Should().Be(field);
        await using var scope = host.Host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.AnyAsync()).Should().BeFalse("nothing was created");
    }

    [Fact]
    public async Task SignIn_ShouldNameAnUnconfirmedEmail_InTheForbiddenProblem()
    {
        await using var host = new AccountHost(new Dictionary<string, string?> { ["Identity:SignIn:IsConfirmedEmailRequired"] = "true" });
        await host.CreateUserAsync("una@example.test", isEmailConfirmed: false);
        using var client = host.CreateClient();

        using var response = await AccountHost.SignInAsync(client, "una@example.test", AccountHost.StrongPassword);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AccountHost.ProblemAsync(response)).GetProperty("identityCode").GetString().Should().Be(IdentityErrorCodeConstants.EmailConfirmationRequired);
    }

    [Fact]
    public async Task SignIn_ShouldNameAnUnconfirmedPhone_InTheForbiddenProblem()
    {
        await using var host = new AccountHost(new Dictionary<string, string?> { ["Identity:SignIn:IsConfirmedPhoneNumberRequired"] = "true" });
        await host.CreateUserAsync("pat@example.test");
        using var client = host.CreateClient();

        using var response = await AccountHost.SignInAsync(client, "pat@example.test", AccountHost.StrongPassword);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AccountHost.ProblemAsync(response)).GetProperty("identityCode").GetString().Should().Be(IdentityErrorCodeConstants.PhoneConfirmationRequired);
    }

    private static string? ReturnUrlOf(Uri link) => HttpUtility.ParseQueryString(link.Query)["returnUrl"];
}

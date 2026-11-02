using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;

namespace Identity.Tests.E2E.Accounts;

/// <summary>
/// The console's gate and the method switches through the real service: <c>GET /api/console/session</c> answers 401 without
/// a sign-in, 403 without an instance role and the person with one; <c>Identity:Methods</c> off turns the password and email
/// routes into 404 <c>SignInMethodOff</c> and drops them from the declaration.
/// </summary>
public sealed class ConsoleAndMethodsApiTests
{
    private const string Console = "/api/console/session";

    [Fact]
    public async Task Console_ShouldAnswer401_403_ThenThePerson_OnceAnInstanceRoleIsHeld()
    {
        await using var host = new AccountHost();
        var userId = await host.CreateUserAsync("lola@example.test");
        using var client = host.CreateClient();

        (await client.GetAsync(Console)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await AccountHost.SignInAsync(client, "lola@example.test", AccountHost.StrongPassword)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync(Console)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "an organization member is no console user");

        await using (var scope = host.Host.Services.CreateAsyncScope())
        {
            var person = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(userId))!;
            (await scope.ServiceProvider.GetRequiredService<InstanceRoleService>().GrantAsync(person, "admin")).IsGranted.Should().BeTrue();
        }

        (await client.GetAsync(Console)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "a role reaches the console from the next sign-in");
        (await AccountHost.SignInAsync(client, "lola@example.test", AccountHost.StrongPassword)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var session = await client.GetAsync(Console);
        var data = await AccountHost.DataAsync(session);
        data.GetProperty("userId").GetString().Should().Be(userId.ToString());
        data.GetProperty("name").GetString().Should().Be("lola@example.test");
        data.GetProperty("roles").EnumerateArray().Select(role => role.GetString()).Should().Equal("admin");
        data.GetProperty("organizations").GetArrayLength().Should().Be(0, "an instance admin manages no organization by that role alone");

        await using (var scope = host.Host.Services.CreateAsyncScope())
        {
            var person = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(userId))!;
            var organization = new IdentityOrganization { Name = "Ocharo", Slug = "ocharo" };
            (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateAsync(organization, person)).IsSuccess.Should().BeTrue();
        }

        var managed = (await AccountHost.DataAsync(await client.GetAsync(Console))).GetProperty("organizations")[0];
        managed.GetProperty("slug").GetString().Should().Be("ocharo");
        managed.GetProperty("role").GetString().Should().Be("owner", "the console invites into the organizations its person owns or administers");
    }

    [Fact]
    public async Task Methods_ShouldTurnThePasswordAndEmailRoutesInto404_AndLeaveThemOutOfTheDeclaration()
    {
        await using var host = new AccountHost(new Dictionary<string, string?>
        {
            ["Identity:Methods:Password:IsEnabled"] = "false",
            ["Identity:Methods:Email:IsEnabled"] = "false",
        });
        await host.CreateUserAsync("lola@example.test");
        using var client = host.CreateClient();

        foreach (var (route, body) in new (string, object)[]
        {
            ("/api/account/sign-in?useCookies=true", new { identifier = "lola@example.test", password = AccountHost.StrongPassword }),
            ("/api/account/sign-up", new { email = "ann@example.test", password = AccountHost.StrongPassword }),
            ("/api/account/forgot-password", new { email = "lola@example.test" }),
            ("/api/account/resend-confirmation-email", new { email = "lola@example.test" }),
        })
        {
            using var response = await client.PostAsJsonAsync(route, body);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, route);
            (await AccountHost.ProblemAsync(response)).GetProperty("identityCode").GetString().Should().Be("SignInMethodOff", route);
        }

        host.Mail.Sent.Should().BeEmpty();
        var options = await AccountHost.DataAsync(await client.GetAsync("/api/account/sign-in-options"));
        options.GetProperty("methods").GetArrayLength().Should().Be(0, "the bot is off here too");
        options.GetProperty("isEmailEnabled").GetBoolean().Should().BeFalse();
    }
}

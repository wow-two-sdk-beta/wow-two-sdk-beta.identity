using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Identity.Tests.E2E.Accounts;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;

namespace Identity.Tests.E2E.Console;

/// <summary>
/// The console reads instance roles from the store on every request, never from the cookie issued at sign-in: a role
/// granted after the sign-in shows in <c>GET /api/console/session</c> and opens <c>POST /api/console/organizations</c> on the
/// next request, a revoked one closes it on the next request, and an organization's owner without an instance role keeps
/// the session but never creates an organization.
/// </summary>
public sealed class ConsoleLiveRoleTests
{
    private const string Session = "/api/console/session";
    private const string Organizations = "/api/console/organizations";

    [Fact]
    public async Task Grant_ShouldShowTheRole_OnTheNextRequest_WithNoSignIn()
    {
        await using var host = new AccountHost();
        var userId = await host.CreateUserAsync("lola@example.test");
        using var client = host.CreateClient();
        (await AccountHost.SignInAsync(client, "lola@example.test", AccountHost.StrongPassword)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync(Session)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "nobody gave her a role yet");

        await GrantAsync(host, userId, InstanceRoleConstants.Admin);

        var session = await AccountHost.DataAsync(await client.GetAsync(Session));
        session.GetProperty("roles").EnumerateArray().Select(role => role.GetString()).Should().Equal("admin");
        (await client.PostAsJsonAsync(Organizations, new { name = "Ocharo", slug = "ocharo" })).StatusCode
            .Should().Be(HttpStatusCode.Created, "the store's role counts at once");
        var managed = (await AccountHost.DataAsync(await client.GetAsync(Session))).GetProperty("organizations").EnumerateArray().Single();
        (managed.GetProperty("slug").GetString(), managed.GetProperty("role").GetString()).Should().Be(("ocharo", "owner"));
    }

    [Fact]
    public async Task Revoke_ShouldAnswer403_OnTheNextRequest_ThoughTheCookieStillCarriesTheRole()
    {
        await using var host = new AccountHost();
        var userId = await host.CreateUserAsync("lola@example.test");
        await GrantAsync(host, userId, InstanceRoleConstants.Admin);
        using var client = host.CreateClient();
        (await AccountHost.SignInAsync(client, "lola@example.test", AccountHost.StrongPassword)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync(Organizations, new { name = "Ocharo", slug = "ocharo" })).StatusCode.Should().Be(HttpStatusCode.Created);

        await RevokeAsync(host, userId, InstanceRoleConstants.Admin);

        (await client.PostAsJsonAsync(Organizations, new { name = "Desk", slug = "desk" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var session = await AccountHost.DataAsync(await client.GetAsync(Session));
        session.GetProperty("roles").GetArrayLength().Should().Be(0, "the session reports the store, not the sign-in");
        session.GetProperty("organizations").EnumerateArray().Select(item => item.GetProperty("slug").GetString())
            .Should().Equal(new[] { "ocharo" }, "she still owns what she created");
        await using var scope = host.Host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<OrganizationService>().FindBySlugAsync("desk")).Should().BeNull();
    }

    [Fact]
    public async Task Revoke_ShouldCloseTheSession_ToAnAdminWhoManagesNoOrganization()
    {
        await using var host = new AccountHost();
        var userId = await host.CreateUserAsync("sam@example.test");
        await GrantAsync(host, userId, InstanceRoleConstants.Admin);
        using var client = host.CreateClient();
        (await AccountHost.SignInAsync(client, "sam@example.test", AccountHost.StrongPassword)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync(Session)).StatusCode.Should().Be(HttpStatusCode.OK);

        await RevokeAsync(host, userId, InstanceRoleConstants.Admin);

        (await client.GetAsync(Session)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Owner_WithoutAnInstanceRole_ShouldOpenTheSession_ButAnswer403ToCreation()
    {
        await using var host = new AccountHost();
        var ownerId = await host.CreateUserAsync("gulnora@example.test");
        await using (var scope = host.Host.Services.CreateAsyncScope())
        {
            var owner = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(ownerId))!;
            var organization = new IdentityOrganization { Name = "Ocharo", Slug = "ocharo" };
            (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateAsync(organization, owner)).IsSuccess.Should().BeTrue();
        }

        using var client = host.CreateClient();
        (await AccountHost.SignInAsync(client, "gulnora@example.test", AccountHost.StrongPassword)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var session = await client.GetAsync(Session);
        session.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await AccountHost.DataAsync(session);
        data.GetProperty("roles").GetArrayLength().Should().Be(0);
        data.GetProperty("organizations").EnumerateArray().Single().GetProperty("role").GetString().Should().Be("owner");
        (await client.PostAsJsonAsync(Organizations, new { name = "Desk", slug = "desk" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Gives the person an instance role in the store, as <c>admin create --role</c> does.</summary>
    private static async Task GrantAsync(AccountHost host, Guid userId, string role)
    {
        await using var scope = host.Host.Services.CreateAsyncScope();
        var person = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(userId))!;
        (await scope.ServiceProvider.GetRequiredService<InstanceRoleService>().GrantAsync(person, role)).IsGranted.Should().BeTrue();
    }

    /// <summary>
    /// Takes the instance role's membership row out of the store and leaves the security stamp alone, so the cookie, which
    /// still carries the role as a claim, stays signed in; <c>UserRoleService.RemoveFromRoleAsync</c> also rotates the stamp
    /// and ends every session on its own.
    /// </summary>
    private static async Task RevokeAsync(AccountHost host, Guid userId, string role)
    {
        await using var scope = host.Host.Services.CreateAsyncScope();
        var row = (await scope.ServiceProvider.GetRequiredService<RoleService>().FindByNameAsync(role))!;
        await scope.ServiceProvider.GetRequiredService<IUserRoleRepository>().RemoveAsync(userId, row.Id);
    }
}

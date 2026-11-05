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
/// a sign-in or after signing out, 403 to a person with neither an instance role nor an organization they own or administer,
/// and the person to either, their instance roles read live; <c>POST /api/console/organizations</c> makes the
/// instance admin an organization's owner, 403 for <c>support</c>, 409 for a taken handle or id and 400 for an invalid one;
/// <c>Identity:Methods</c> off turns the password and email routes into 404 <c>SignInMethodOff</c> and drops them from the
/// declaration.
/// </summary>
public sealed class ConsoleAndMethodsApiTests
{
    private const string Console = "/api/console/session";
    private const string Organizations = "/api/console/organizations";
    private const string DeskId = "0190a2b3-c4d5-4e6f-8a9b-0c1d2e3f4a5b";

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

        using var session = await client.GetAsync(Console);
        session.StatusCode.Should().Be(HttpStatusCode.OK, "the console reads the role from the store");
        var data = await AccountHost.DataAsync(session);
        data.GetProperty("userId").GetString().Should().Be(userId.ToString());
        data.GetProperty("name").GetString().Should().Be("lola@example.test");
        data.GetProperty("roles").EnumerateArray().Select(role => role.GetString()).Should().Equal("admin");
        data.GetProperty("organizations").GetArrayLength().Should().Be(0, "an instance admin manages no organization by that role alone");

        (await client.PostAsJsonAsync(Organizations, new { name = "Ocharo", slug = "ocharo" })).StatusCode.Should().Be(HttpStatusCode.Created);

        var managed = (await AccountHost.DataAsync(await client.GetAsync(Console))).GetProperty("organizations")[0];
        managed.GetProperty("slug").GetString().Should().Be("ocharo");
        managed.GetProperty("role").GetString().Should().Be("owner", "the console invites into the organizations its person owns or administers");
    }

    [Fact]
    public async Task Console_ShouldOpenToAnOrganizationsOwner_WithoutAnInstanceRole_ButRefuseThemOrganizationCreation()
    {
        await using var host = new AccountHost();
        var ownerId = await host.CreateUserAsync("gulnora@example.test");
        var editorId = await host.CreateUserAsync("bobur@example.test");
        await using (var scope = host.Host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<UserAccountService>();
            var organizations = scope.ServiceProvider.GetRequiredService<OrganizationService>();
            var organization = new IdentityOrganization { Id = Guid.Parse(DeskId), Name = "Ocharo", Slug = "ocharo" };
            (await organizations.CreateAsync(organization, (await accounts.FindByIdAsync(ownerId))!)).IsSuccess.Should().BeTrue();
            (await organizations.AddMemberAsync(organization, (await accounts.FindByIdAsync(editorId))!, "editor")).IsSuccess.Should().BeTrue();
        }

        using var owner = host.CreateClient();
        (await AccountHost.SignInAsync(owner, "gulnora@example.test", AccountHost.StrongPassword)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var session = await owner.GetAsync(Console);
        session.StatusCode.Should().Be(HttpStatusCode.OK, "an owner invites into their organization from the console");
        var data = await AccountHost.DataAsync(session);
        data.GetProperty("roles").GetArrayLength().Should().Be(0);
        var managed = data.GetProperty("organizations").EnumerateArray().Single();
        (managed.GetProperty("id").GetString(), managed.GetProperty("role").GetString()).Should().Be((DeskId, "owner"));
        (await owner.PostAsJsonAsync(Organizations, new { name = "Ocharo Two", slug = "ocharo-two" })).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "creating an organization is the instance admin's");

        using var editor = host.CreateClient();
        (await AccountHost.SignInAsync(editor, "bobur@example.test", AccountHost.StrongPassword)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await editor.GetAsync(Console)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "an editor manages nothing");
    }

    [Fact]
    public async Task CreateOrganization_ShouldMakeTheAdminItsOwner_KeepingAPresetId()
    {
        await using var host = new AccountHost();
        using var client = await SignInWithRoleAsync(host, "lola@example.test", InstanceRoleConstants.Admin);

        using var created = await client.PostAsJsonAsync(Organizations, new { name = " Ocharo Desk ", slug = "Desk", id = DeskId });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var organization = await AccountHost.DataAsync(created);
        organization.GetProperty("id").GetString().Should().Be(DeskId, "a product keeps its existing key");
        organization.GetProperty("name").GetString().Should().Be("Ocharo Desk");
        organization.GetProperty("slug").GetString().Should().Be("desk");
        organization.GetProperty("role").GetString().Should().Be("owner");
        var listed = (await AccountHost.DataAsync(await client.GetAsync(Console))).GetProperty("organizations");
        listed.EnumerateArray().Select(item => item.GetProperty("id").GetString()).Should().Equal(DeskId);

        using var assigned = await client.PostAsJsonAsync(Organizations, new { name = "Ocharo", slug = "ocharo" });
        assigned.StatusCode.Should().Be(HttpStatusCode.Created);
        Guid.Parse((await AccountHost.DataAsync(assigned)).GetProperty("id").GetString()!).Version.Should().Be(7, "an absent id is assigned");
    }

    [Theory]
    [InlineData("Ocharo", "desk", null, HttpStatusCode.Conflict, "DuplicateOrganizationSlug")]
    [InlineData("Ocharo", "ocharo", DeskId, HttpStatusCode.Conflict, "DuplicateOrganizationId")]
    [InlineData("Ocharo", "not a handle", null, HttpStatusCode.BadRequest, "InvalidOrganizationSlug")]
    [InlineData(" ", "ocharo", null, HttpStatusCode.BadRequest, "OrganizationNameRequired")]
    public async Task CreateOrganization_ShouldRefuse_ATakenOrInvalidHandleOrId(string name, string slug, string? id, HttpStatusCode status, string code)
    {
        await using var host = new AccountHost();
        using var client = await SignInWithRoleAsync(host, "lola@example.test", InstanceRoleConstants.Admin);
        (await client.PostAsJsonAsync(Organizations, new { name = "Desk", slug = "desk", id = DeskId })).StatusCode.Should().Be(HttpStatusCode.Created);

        using var refused = await client.PostAsJsonAsync(Organizations, new { name, slug, id });

        refused.StatusCode.Should().Be(status);
        (await AccountHost.ProblemAsync(refused)).GetProperty("identityCode").GetString().Should().Be(code);
        (await AccountHost.DataAsync(await client.GetAsync(Console))).GetProperty("organizations").GetArrayLength().Should().Be(1, "a refusal creates nothing");
    }

    [Theory]
    [InlineData(InstanceRoleConstants.Support)]
    [InlineData(InstanceRoleConstants.Auditor)]
    public async Task CreateOrganization_ShouldAnswer403_ToAConsoleUserWhoIsNoAdmin(string role)
    {
        await using var host = new AccountHost();
        using var client = await SignInWithRoleAsync(host, "sam@example.test", role);
        (await client.GetAsync(Console)).StatusCode.Should().Be(HttpStatusCode.OK, "the role opens the console");

        (await client.PostAsJsonAsync(Organizations, new { name = "Ocharo", slug = "ocharo" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var anonymous = host.CreateClient();
        (await anonymous.PostAsJsonAsync(Organizations, new { name = "Ocharo", slug = "ocharo" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await using var scope = host.Host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<OrganizationService>().FindBySlugAsync("ocharo")).Should().BeNull();
    }

    [Fact]
    public async Task SignOut_ShouldEndTheConsoleSession()
    {
        await using var host = new AccountHost();
        using var client = await SignInWithRoleAsync(host, "lola@example.test", InstanceRoleConstants.Admin);
        (await client.GetAsync(Console)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PostAsync("/api/account/sign-out", content: null)).StatusCode.Should().Be(HttpStatusCode.NoContent, "the console signs out with an empty POST");

        (await client.GetAsync(Console)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync(Organizations, new { name = "Ocharo", slug = "ocharo" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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

    /// <summary>A client signed in as a new person holding the instance role <paramref name="role"/>, given before the sign-in.</summary>
    private static async Task<HttpClient> SignInWithRoleAsync(AccountHost host, string email, string role)
    {
        var userId = await host.CreateUserAsync(email);
        await using (var scope = host.Host.Services.CreateAsyncScope())
        {
            var person = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(userId))!;
            (await scope.ServiceProvider.GetRequiredService<InstanceRoleService>().GrantAsync(person, role)).IsGranted.Should().BeTrue();
        }

        var client = host.CreateClient();
        (await AccountHost.SignInAsync(client, email, AccountHost.StrongPassword)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return client;
    }
}

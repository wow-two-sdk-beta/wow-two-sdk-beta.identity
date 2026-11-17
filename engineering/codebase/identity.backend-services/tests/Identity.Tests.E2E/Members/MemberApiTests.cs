using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AwesomeAssertions;
using Identity.Tests.E2E.Notices;
using Identity.Tests.E2E.PhoneSignIn;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Members;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Identity.Tests.E2E.Members;

/// <summary>
/// Organization members through the real service with the real OIDC server: a client granted <c>identity.members.read</c>
/// lists an organization's members with their role and whether the bot reaches them, or one role's; a client without the scope
/// gets <c>403</c>, no token <c>401</c>; an unknown organization <c>404</c>, a role outside the catalog <c>400</c>; a client
/// bound to one organization reads only that one.
/// </summary>
public sealed class MemberApiTests
{
    private const string Audience = "https://localhost/api/identity/";
    private const string Events = "e2e-events";
    private const string Reader = "e2e-reader";
    private const string Bound = "e2e-bound";
    private const string Secret = "e2e-test-only-notice-secret";
    private static readonly Guid BoundOrganization = Guid.Parse("0193a000-0000-7000-8000-00000000b0b0");

    [Fact]
    public async Task ScopedClient_ShouldListMembersWithRoleAndTelegramFlag_AndOthersBeRefused()
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync();
        try
        {
            await using var host = CreateHost(connectionString);
            using var http = AppNoticeApiTests.Client(host);
            var owner = await AppNoticeApiTests.CreatePersonAsync(host, "+998907777777", 9101);
            var editor = await AppNoticeApiTests.CreatePersonAsync(host, "+998905555555", 9202);
            var unlinked = await AppNoticeApiTests.CreatePersonAsync(host, "+998901111111", chatId: null);
            var organization = await CreateOrganizationAsync(host, owner, (editor, "editor"), (unlinked, "viewer"));
            await CreateOrganizationAsync(host, unlinked);

            (await ListAsync(http, null, organization)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            using (var unscoped = await ListAsync(http, await AppNoticeApiTests.TokenAsync(http, Reader, "identity.people.read"), organization))
                unscoped.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a client the declaration grants no identity.members.read");

            var token = await AppNoticeApiTests.TokenAsync(http, Events, MemberConstants.Scope);
            var all = await MembersAsync(await ListAsync(http, token, organization));
            all.Select(Row).Should().Equal((owner.ToString(), "owner", true), (editor.ToString(), "editor", true), (unlinked.ToString(), "viewer", false));
            all.Should().AllSatisfy(member => member.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("sub", "role", "hasTelegramLink"));

            (await MembersAsync(await ListAsync(http, token, organization, "Editor"))).Select(Row).Should().Equal((editor.ToString(), "editor", true));
            (await CodeOfAsync(await ListAsync(http, token, organization, "member"), HttpStatusCode.BadRequest)).Should().Be(IdentityErrorCodeConstants.UnknownOrganizationRole);
            (await CodeOfAsync(await ListAsync(http, token, Guid.NewGuid()), HttpStatusCode.NotFound)).Should().Be(IdentityErrorCodeConstants.OrganizationNotFound);
        }
        finally
        {
            await PostgresTestServer.DropDatabaseAsync(connectionString);
        }
    }

    [Fact]
    public async Task BoundClient_ShouldReadOnlyItsOwnOrganization()
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync();
        try
        {
            await using var host = CreateHost(connectionString);
            using var http = AppNoticeApiTests.Client(host);
            var owner = await AppNoticeApiTests.CreatePersonAsync(host, "+998907777777", 9101);
            var own = await CreateOrganizationAsync(host, owner, id: BoundOrganization);
            var other = await CreateOrganizationAsync(host, owner);
            var token = await AppNoticeApiTests.TokenAsync(http, Bound, MemberConstants.Scope);

            (await MembersAsync(await ListAsync(http, token, own))).Select(Row).Should().Equal((owner.ToString(), "owner", true));
            (await CodeOfAsync(await ListAsync(http, token, other), HttpStatusCode.NotFound)).Should().Be(IdentityErrorCodeConstants.OrganizationNotFound);
        }
        finally
        {
            await PostgresTestServer.DropDatabaseAsync(connectionString);
        }
    }

    /// <summary>The notice host with the members route on the same resource: client 0 may read members, client 2 is bound to one organization.</summary>
    private static WebApiTestHost<Program> CreateHost(string connectionString) => AppNoticeApiTests.CreateHost(connectionString, new FakeTelegramBotClient(), new Dictionary<string, string?>
    {
        ["Identity:Members:Audience"] = Audience,
        ["Identity:Server:Resources:0:Scopes:2"] = MemberConstants.Scope,
        ["Identity:Server:Clients:0:Scopes:1"] = MemberConstants.Scope,
        ["Identity:Server:Clients:2:ClientId"] = Bound,
        ["Identity:Server:Clients:2:ClientSecret"] = Secret,
        ["Identity:Server:Clients:2:Grants:0"] = "client_credentials",
        ["Identity:Server:Clients:2:Resources:0"] = "identity",
        ["Identity:Server:Clients:2:Scopes:0"] = MemberConstants.Scope,
        ["Identity:Server:Clients:2:OrganizationId"] = BoundOrganization.ToString(),
    });

    /// <summary>An organization <paramref name="owner"/> founds, then the members join in order, a second apart.</summary>
    private static async Task<Guid> CreateOrganizationAsync(WebApiTestHost<Program> host, Guid owner, params (Guid User, string Role)[] members)
        => await CreateOrganizationAsync(host, owner, null, members);

    private static async Task<Guid> CreateOrganizationAsync(WebApiTestHost<Program> host, Guid owner, Guid? id, params (Guid User, string Role)[] members)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var organizations = scope.ServiceProvider.GetRequiredService<OrganizationService>();
        var accounts = scope.ServiceProvider.GetRequiredService<UserAccountService>();
        var organization = new IdentityOrganization { Id = id ?? Guid.Empty, Name = "Ocharo", Slug = "org-" + Guid.NewGuid().ToString("N")[..12] };
        (await organizations.CreateAsync(organization, (await accounts.FindByIdAsync(owner))!)).IsSuccess.Should().BeTrue();
        foreach (var (user, role) in members)
        {
            host.Clock.Advance(TimeSpan.FromSeconds(1));
            (await organizations.AddMemberAsync(organization, (await accounts.FindByIdAsync(user))!, role)).IsSuccess.Should().BeTrue();
        }

        return organization.Id;
    }

    private static (string?, string?, bool) Row(JsonElement member)
        => (member.GetProperty("sub").GetString(), member.GetProperty("role").GetString(), member.GetProperty("hasTelegramLink").GetBoolean());

    private static async Task<HttpResponseMessage> ListAsync(HttpClient http, string? token, Guid organization, string? role = null)
    {
        var path = MemberConstants.PathOf(organization) + (role is null ? string.Empty : $"?role={Uri.EscapeDataString(role)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request);
    }

    private static async Task<List<JsonElement>> MembersAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.OK, body);
            using var document = JsonDocument.Parse(body);
            return [.. document.RootElement.GetProperty("data").EnumerateArray().Select(member => member.Clone())];
        }
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(expected, body);
            using var problem = JsonDocument.Parse(body);
            return problem.RootElement.TryGetProperty(MemberConstants.CodeMetadataKey, out var code) ? code.GetString() : body;
        }
    }
}

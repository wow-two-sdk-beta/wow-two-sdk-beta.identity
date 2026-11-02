using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

namespace Identity.Tests.E2E.Accounts;

/// <summary>
/// Invitations through the real API while sign-up is by invitation only: an owner or admin makes a link with a role, the
/// link's preview names the organization and role, it signs one person up into that role or adds a signed-in person, and
/// the managers' member routes guard owners and the last owner.
/// </summary>
public sealed class InvitationApiTests
{
    private static readonly Dictionary<string, string?> InviteOnly = new()
    {
        ["Identity:SignUp:Mode"] = "Invite",
        ["Identity:Organizations:Roles:admin:0"] = "members:manage",
        ["Identity:Organizations:Roles:editor:0"] = "content:edit",
    };

    [Fact]
    public async Task Link_ShouldSignUpTheNamedEmail_IntoItsRole_Once()
    {
        await using var host = new AccountHost(InviteOnly);
        var (organizationId, owner, _) = await OrganizationAsync(host, "owner-a@example.test");

        var created = await AccountHost.DataAsync(await owner.PostAsJsonAsync($"/api/organizations/{organizationId}/invitations", new { role = "editor", email = "Nodir@Example.test" }));
        var token = created.GetProperty("token").GetString()!;
        created.GetProperty("link").GetString().Should().Be($"https://id.example.test/sign-up?invitation={token}");
        created.GetProperty("invitation").GetProperty("status").GetString().Should().Be("pending");

        using var visitor = host.CreateClient();
        using (var closed = await visitor.PostAsJsonAsync("/api/account/sign-up", new { email = "walk-in@example.test", password = AccountHost.StrongPassword }))
        {
            closed.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await AccountHost.ProblemAsync(closed)).GetProperty("identityCode").GetString().Should().Be("SignUpClosed");
        }

        var preview = await AccountHost.DataAsync(await visitor.GetAsync($"/api/account/invitations/{token}"));
        preview.GetProperty("organizationName").GetString().Should().Be("Ocharo");
        preview.GetProperty("role").GetString().Should().Be("editor");
        preview.GetProperty("status").GetString().Should().Be("pending");
        preview.GetProperty("emailHint").GetString().Should().Be("n***@example.test");
        (await visitor.GetAsync("/api/account/invitations/unknown-token")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using (var other = await visitor.PostAsJsonAsync("/api/account/sign-up", new { email = "other@example.test", password = AccountHost.StrongPassword, invitationToken = token }))
        {
            other.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await AccountHost.ProblemAsync(other)).GetProperty("identityCode").GetString().Should().Be("InvitationIdentifierMismatch");
        }

        var signedUp = await AccountHost.DataAsync(await visitor.PostAsJsonAsync("/api/account/sign-up", new { email = "nodir@example.test", password = AccountHost.StrongPassword, invitationToken = token }));
        var nodir = Guid.Parse(signedUp.GetProperty("userId").GetString()!);
        (await RoleAsync(host, organizationId, nodir)).Should().Be("editor");

        using (var again = await visitor.PostAsJsonAsync("/api/account/sign-up", new { email = "nodir@example.test", password = AccountHost.StrongPassword, invitationToken = token }))
        {
            again.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await AccountHost.ProblemAsync(again)).GetProperty("identityCode").GetString().Should().Be("InvitationAccepted");
        }

        var members = await AccountHost.DataAsync(await owner.GetAsync($"/api/organizations/{organizationId}/members"));
        members.EnumerateArray().Select(m => (m.GetProperty("email").GetString(), m.GetProperty("role").GetString()))
            .Should().BeEquivalentTo([("owner-a@example.test", "owner"), ("nodir@example.test", "editor")]);
        var listed = await AccountHost.DataAsync(await owner.GetAsync($"/api/organizations/{organizationId}/invitations"));
        listed.EnumerateArray().Single().GetProperty("status").GetString().Should().Be("accepted");
    }

    [Fact]
    public async Task Managers_ShouldBeOwnersAndAdmins_AndOnlyAnOwnerHandlesOwners()
    {
        await using var host = new AccountHost(InviteOnly);
        var (organizationId, owner, ownerId) = await OrganizationAsync(host, "owner-b@example.test");

        var adminLink = await AccountHost.DataAsync(await owner.PostAsJsonAsync($"/api/organizations/{organizationId}/invitations", new { role = "admin" }));
        var (adminId, admin) = await SignUpAsync(host, "admin-b@example.test", adminLink.GetProperty("token").GetString()!);
        (await RoleAsync(host, organizationId, adminId)).Should().Be("admin", "a link naming no identifier takes anyone holding it");

        await ExpectAsync(await admin.PostAsJsonAsync($"/api/organizations/{organizationId}/invitations", new { role = "owner" }), HttpStatusCode.Forbidden, "OrganizationOwnerRequired");
        await ExpectAsync(await admin.PutAsJsonAsync($"/api/organizations/{organizationId}/members/{ownerId}", new { role = "editor" }), HttpStatusCode.Forbidden, "OrganizationOwnerRequired");
        var editorLink = await AccountHost.DataAsync(await admin.PostAsJsonAsync($"/api/organizations/{organizationId}/invitations", new { role = "editor" }));
        var (editorId, editor) = await SignUpAsync(host, "editor-b@example.test", editorLink.GetProperty("token").GetString()!);

        await ExpectAsync(await editor.GetAsync($"/api/organizations/{organizationId}/members"), HttpStatusCode.Forbidden, "OrganizationManagerRequired");
        using var outsider = host.CreateClient();
        await host.CreateUserAsync("outsider-b@example.test");
        (await AccountHost.SignInAsync(outsider, "outsider-b@example.test", AccountHost.StrongPassword)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await ExpectAsync(await outsider.GetAsync($"/api/organizations/{organizationId}/members"), HttpStatusCode.NotFound, "OrganizationNotFound");
        (await host.CreateClient().GetAsync($"/api/organizations/{organizationId}/members")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await ExpectAsync(await owner.PutAsJsonAsync($"/api/organizations/{organizationId}/members/{ownerId}", new { role = "editor" }), HttpStatusCode.Conflict, "LastOrganizationOwner");
        await ExpectAsync(await owner.DeleteAsync($"/api/organizations/{organizationId}/members/{ownerId}"), HttpStatusCode.Conflict, "LastOrganizationOwner");
        (await admin.PutAsJsonAsync($"/api/organizations/{organizationId}/members/{editorId}", new { role = "admin" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await RoleAsync(host, organizationId, editorId)).Should().Be("admin");
        await ExpectAsync(await owner.PutAsJsonAsync($"/api/organizations/{organizationId}/members/{editorId}", new { role = "chief" }), HttpStatusCode.BadRequest, "UnknownOrganizationRole");
        (await owner.DeleteAsync($"/api/organizations/{organizationId}/members/{adminId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await RoleAsync(host, organizationId, adminId)).Should().BeNull();
        await ExpectAsync(await owner.DeleteAsync($"/api/organizations/{organizationId}/members/{adminId}"), HttpStatusCode.NotFound, "UserNotMember");
    }

    [Fact]
    public async Task Link_ShouldAddASignedInPerson_UntilWithdrawnOrExpired()
    {
        await using var host = new AccountHost(InviteOnly);
        var (organizationId, owner, _) = await OrganizationAsync(host, "owner-c@example.test");
        var guestId = await host.CreateUserAsync("guest-c@example.test");
        using var guest = host.CreateClient();
        (await AccountHost.SignInAsync(guest, "guest-c@example.test", AccountHost.StrongPassword)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var join = await AccountHost.DataAsync(await owner.PostAsJsonAsync($"/api/organizations/{organizationId}/invitations", new { role = "editor", email = "guest-c@example.test" }));
        (await host.CreateClient().PostAsync($"/api/account/invitations/{join.GetProperty("token").GetString()}/accept", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await guest.PostAsync($"/api/account/invitations/{join.GetProperty("token").GetString()}/accept", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await RoleAsync(host, organizationId, guestId)).Should().Be("editor");
        (await guest.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.OK, "the reissued cookie still signs in");

        var withdrawn = await AccountHost.DataAsync(await owner.PostAsJsonAsync($"/api/organizations/{organizationId}/invitations", new { role = "editor" }));
        var withdrawnId = withdrawn.GetProperty("invitation").GetProperty("id").GetString();
        (await owner.DeleteAsync($"/api/organizations/{organizationId}/invitations/{withdrawnId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await ExpectAsync(await owner.DeleteAsync($"/api/organizations/{organizationId}/invitations/{withdrawnId}"), HttpStatusCode.NotFound, "InvitationNotFound");
        (await AccountHost.DataAsync(await guest.GetAsync($"/api/account/invitations/{withdrawn.GetProperty("token").GetString()}"))).GetProperty("status").GetString().Should().Be("revoked");
        await ExpectAsync(
            await host.CreateClient().PostAsJsonAsync("/api/account/sign-up", new { email = "late@example.test", password = AccountHost.StrongPassword, invitationToken = withdrawn.GetProperty("token").GetString() }),
            HttpStatusCode.Conflict,
            "InvitationRevoked");

        var late = await AccountHost.DataAsync(await owner.PostAsJsonAsync($"/api/organizations/{organizationId}/invitations", new { role = "editor" }));
        host.Host.Clock.Advance(TimeSpan.FromDays(8));
        (await AccountHost.SignInAsync(guest, "guest-c@example.test", AccountHost.StrongPassword)).StatusCode.Should().Be(HttpStatusCode.NoContent, "the earlier session ended meanwhile");
        await ExpectAsync(await guest.PostAsync($"/api/account/invitations/{late.GetProperty("token").GetString()}/accept", null), HttpStatusCode.Conflict, "InvitationExpired");
    }

    /// <summary>Creates a confirmed owner, signs them in and makes their organization; returns its id, the owner's client and id.</summary>
    private static async Task<(Guid OrganizationId, HttpClient Owner, Guid OwnerId)> OrganizationAsync(AccountHost host, string email)
    {
        var ownerId = await host.CreateUserAsync(email);
        var organization = new IdentityOrganization { Name = "Ocharo", Slug = "ocharo-" + Guid.NewGuid().ToString("N")[..8] };
        await using (var scope = host.Host.Services.CreateAsyncScope())
        {
            var user = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(ownerId))!;
            (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateAsync(organization, user)).IsSuccess.Should().BeTrue();
        }

        var owner = host.CreateClient();
        (await AccountHost.SignInAsync(owner, email, AccountHost.StrongPassword)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return (organization.Id, owner, ownerId);
    }

    /// <summary>Signs a new person up with the link's token, marks their email confirmed, and signs them in.</summary>
    private static async Task<(Guid UserId, HttpClient Client)> SignUpAsync(AccountHost host, string email, string token)
    {
        var client = host.CreateClient();
        var signedUp = await AccountHost.DataAsync(await client.PostAsJsonAsync("/api/account/sign-up", new { email, password = AccountHost.StrongPassword, invitationToken = token }));
        var userId = Guid.Parse(signedUp.GetProperty("userId").GetString()!);
        (await AccountHost.SignInAsync(client, email, AccountHost.StrongPassword)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return (userId, client);
    }

    private static async Task<string?> RoleAsync(AccountHost host, Guid organizationId, Guid userId)
    {
        await using var scope = host.Host.Services.CreateAsyncScope();
        var organizations = scope.ServiceProvider.GetRequiredService<OrganizationService>();
        var organization = (await organizations.FindByIdAsync(organizationId))!;
        var user = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(userId))!;
        return await organizations.GetRoleAsync(organization, user);
    }

    private static async Task ExpectAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
            (await AccountHost.ProblemAsync(response)).GetProperty("identityCode").GetString().Should().Be(code);
        }
    }
}

using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

/// <summary>
/// Invitations into an organization: made with a role and an optional phone or email, found by the link's token, spent
/// once for the identifier they name, refused once expired or withdrawn, and redeemed inside a sign-up's transaction.
/// </summary>
public sealed class InvitationTests
{
    private static AccountsHost CreateHost() => AccountsHost.Create(identity => identity
        .AddOrganizations(o =>
        {
            o.Roles["admin"] = ["members:manage"];
            o.Roles["editor"] = ["content:edit"];
        })
        .AddInvitations(o => o.LinkTemplate = "https://id.example.test/sign-up?invitation={token}"));

    [Fact]
    public async Task Create_ShouldStoreOnlyTheDigest_NormalizeTheIdentifiers_AndRefuseUnknownRolesAndBadIdentifiers()
    {
        await using var host = CreateHost();
        var (owner, organization) = await OrganizationAsync(host);
        await using var scope = host.Scope();
        var invitations = scope.ServiceProvider.GetRequiredService<InvitationService>();

        var created = await invitations.CreateAsync(organization, owner, "EDITOR", " +998 90 123 45 67 ", " Ann@Example.Test ");

        created.IsSuccess.Should().BeTrue();
        created.Invitation!.Role.Should().Be("editor", "the role takes its configured spelling");
        created.Invitation.PhoneNumber.Should().Be("+998901234567");
        created.Invitation.Email.Should().Be("ann@example.test");
        created.Invitation.ExpiresAt.Should().Be(host.Time.GetUtcNow().AddDays(7));
        created.Link.Should().Be("https://id.example.test/sign-up?invitation=" + created.Token);
        created.Invitation.TokenHash.Should().NotContain(created.Token!).And.HaveLength(64);

        (await invitations.CreateAsync(organization, owner, "chief", null, null)).Result.Errors[0].Code.Should().Be(IdentityErrorCodeConstants.UnknownOrganizationRole);
        (await invitations.CreateAsync(organization, owner, "editor", "12", null)).Result.Errors[0].Code.Should().Be(IdentityErrorCodeConstants.InvalidInvitationIdentifier);
        (await invitations.CreateAsync(organization, owner, "editor", null, "not-an-email")).Result.Errors[0].Code.Should().Be(IdentityErrorCodeConstants.InvalidInvitationIdentifier);
    }

    [Fact]
    public async Task Redeem_ShouldJoinOnce_WithTheRole_AndOnlyForTheNamedIdentifier()
    {
        await using var host = CreateHost();
        var (owner, organization) = await OrganizationAsync(host);
        var stranger = await host.CreateUserAsync("stranger", "stranger@example.test");
        var ann = await host.CreateUserAsync("ann", "ann@example.test");
        await using var scope = host.Scope();
        var invitations = scope.ServiceProvider.GetRequiredService<InvitationService>();
        var organizations = scope.ServiceProvider.GetRequiredService<OrganizationService>();
        var created = await invitations.CreateAsync(organization, owner, "editor", null, "ann@example.test");

        var lookup = await invitations.FindAsync(created.Token);
        lookup!.Status.Should().Be(InvitationStatus.Pending);
        lookup.Organization.Id.Should().Be(organization.Id);
        (await invitations.FindAsync("not-a-token")).Should().BeNull();

        (await invitations.RedeemAsync(lookup.Invitation, stranger)).Errors[0].Code.Should().Be(IdentityErrorCodeConstants.InvitationIdentifierMismatch);
        (await invitations.RedeemAsync(lookup.Invitation, ann)).IsSuccess.Should().BeTrue();
        (await organizations.GetRoleAsync(organization, ann)).Should().Be("editor");

        var spent = await invitations.FindAsync(created.Token);
        spent!.Status.Should().Be(InvitationStatus.Accepted);
        spent.Invitation.AcceptedByUserId.Should().Be(ann.Id);
        (await invitations.RedeemAsync(lookup.Invitation, ann)).Errors[0].Code.Should().Be(IdentityErrorCodeConstants.InvitationAccepted, "a stale copy cannot spend it twice");
    }

    [Fact]
    public async Task Redeem_ShouldNeedAConfirmedPhone_KeepAMembersRole_AndRefuseExpiredOrWithdrawnInvitations()
    {
        await using var host = CreateHost();
        var (owner, organization) = await OrganizationAsync(host);
        await using var scope = host.Scope();
        var invitations = scope.ServiceProvider.GetRequiredService<InvitationService>();
        var organizations = scope.ServiceProvider.GetRequiredService<OrganizationService>();

        var byPhone = await invitations.CreateAsync(organization, owner, "editor", "+998901234567", null);
        var unconfirmed = new IdentityUser { UserName = "lola", PhoneNumber = "+998901234567" };
        (await scope.ServiceProvider.GetRequiredService<UserAccountService>().CreateAsync(unconfirmed)).IsSuccess.Should().BeTrue();
        (await invitations.RedeemAsync(byPhone.Invitation!, unconfirmed)).Errors[0].Code.Should().Be(IdentityErrorCodeConstants.InvitationIdentifierMismatch, "only a confirmed number counts");

        var promotion = await invitations.CreateAsync(organization, owner, "admin", null, null);
        (await invitations.RedeemAsync(promotion.Invitation!, owner)).IsSuccess.Should().BeTrue();
        (await organizations.GetRoleAsync(organization, owner)).Should().Be("owner", "a member keeps their role");

        var withdrawn = await invitations.CreateAsync(organization, owner, "editor", null, null);
        (await invitations.RevokeAsync(organization, withdrawn.Invitation!.Id)).Should().BeTrue();
        (await invitations.RevokeAsync(organization, withdrawn.Invitation.Id)).Should().BeFalse();
        (await invitations.FindAsync(withdrawn.Token))!.Status.Should().Be(InvitationStatus.Revoked);

        var late = await invitations.CreateAsync(organization, owner, "editor", null, null);
        host.Time.Advance(TimeSpan.FromDays(7));
        var expired = await invitations.FindAsync(late.Token);
        expired!.Status.Should().Be(InvitationStatus.Expired);
        (await invitations.RedeemAsync(expired.Invitation, await host.CreateUserAsync("late"))).Errors[0].Code.Should().Be(IdentityErrorCodeConstants.InvitationExpired);
        (await invitations.ListAsync(organization)).Should().HaveCount(4);
    }

    [Fact]
    public async Task RedeemWith_ShouldUndoTheSignUp_WhenTheInvitationIsSpentMeanwhile()
    {
        await using var host = CreateHost();
        var (owner, organization) = await OrganizationAsync(host);
        var first = await host.CreateUserAsync("first");
        await using var scope = host.Scope();
        var invitations = scope.ServiceProvider.GetRequiredService<InvitationService>();
        var accounts = scope.ServiceProvider.GetRequiredService<UserAccountService>();
        var created = await invitations.CreateAsync(organization, owner, "editor", null, null);
        var stale = (await invitations.FindAsync(created.Token))!.Invitation;
        (await invitations.RedeemAsync(stale, first)).IsSuccess.Should().BeTrue();

        var late = new IdentityUser { UserName = "second", Email = "second@example.test" };
        var (step, redemption) = await invitations.RedeemWithAsync(stale, token => accounts.CreateAsync(late, token), result => result.IsSuccess ? late : null);

        step.IsSuccess.Should().BeTrue();
        redemption!.Errors[0].Code.Should().Be(IdentityErrorCodeConstants.InvitationAccepted);
        await using var check = host.Scope();
        (await check.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.AnyAsync(u => u.UserName == "second")).Should().BeFalse("the refused redemption undid the sign-up");
    }

    private static async Task<(IdentityUser Owner, IdentityOrganization Organization)> OrganizationAsync(AccountsHost host)
    {
        var owner = await host.CreateUserAsync("owner");
        await using var scope = host.Scope();
        var organization = new IdentityOrganization { Name = "Ocharo", Slug = "ocharo" };
        (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateAsync(organization, owner)).IsSuccess.Should().BeTrue();
        return (owner, organization);
    }
}

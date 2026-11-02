using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;
using WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Admin;

/// <summary>
/// <see cref="AdminInvitationService"/> over PostgreSQL, the logic behind <c>admin invite</c>: an invitation found by the
/// organization's handle or id, with any catalog role, an owner included, an optional phone in the allowed countries, and
/// the link that redeems it.
/// </summary>
public sealed class AdminInvitationTests
{
    [Fact]
    public async Task Create_ShouldInviteAnOwner_ByHandleOrId_WithTheLink_RecordedAsTheHost()
    {
        await using var host = CreateHost();
        var organization = await OrganizationAsync(host);

        var byHandle = await CreateAsync(host, new AdminInvitationRequest { Organization = "OCHARO", Role = "Owner", PhoneNumber = "+998 90 123 45 67" });
        var byId = await CreateAsync(host, new AdminInvitationRequest { Organization = organization.Id.ToString(), Role = "editor" });

        byHandle.Should().BeEquivalentTo(new { Status = AdminInvitationStatus.Created, OrganizationId = organization.Id, Role = "owner" }, options => options.ExcludingMissingMembers());
        byHandle.Link.Should().Be("https://id.example.test/sign-up?invitation=" + byHandle.Token);
        byHandle.ExpiresAt.Should().Be(host.Time.GetUtcNow().AddDays(7));
        byId.Should().BeEquivalentTo(new { Status = AdminInvitationStatus.Created, Role = "editor" }, options => options.ExcludingMissingMembers());

        await using var scope = host.Scope();
        var lookup = await scope.ServiceProvider.GetRequiredService<InvitationService>().FindAsync(byHandle.Token);
        lookup!.Invitation.Should().BeEquivalentTo(new { PhoneNumber = "+998901234567", CreatedByUserId = Guid.Empty }, options => options.ExcludingMissingMembers());
        lookup.Status.Should().Be(InvitationStatus.Pending);
    }

    [Fact]
    public async Task Create_ShouldRefuseAnUnknownOrganization_AndFailABadRoleOrPhone()
    {
        await using var host = CreateHost();
        await OrganizationAsync(host);

        (await CreateAsync(host, new AdminInvitationRequest { Organization = "nowhere", Role = "owner" })).Status.Should().Be(AdminInvitationStatus.Refused);
        (await CreateAsync(host, new AdminInvitationRequest { Organization = "ocharo", Role = "chief" })).Status.Should().Be(AdminInvitationStatus.Invalid);
        (await CreateAsync(host, new AdminInvitationRequest { Organization = "ocharo", Role = "owner", PhoneNumber = "12" })).Status.Should().Be(AdminInvitationStatus.Invalid);
        var foreign = await CreateAsync(host, new AdminInvitationRequest { Organization = "ocharo", Role = "owner", PhoneNumber = "+77011234567" });
        foreign.Status.Should().Be(AdminInvitationStatus.Invalid);
        foreign.Error.Should().Contain("Identity:Phone:AllowedCountries");
    }

    private static AccountsHost CreateHost() => AccountsHost.Create(identity => identity
        .AddOrganizations()
        .AddInvitations(o => o.LinkTemplate = "https://id.example.test/sign-up?invitation={token}")
        .AddPhoneNumbers(phones => phones.AllowedCountries = ["UZ"]));

    private static async Task<IdentityOrganization> OrganizationAsync(AccountsHost host)
    {
        var owner = await host.CreateUserAsync("owner");
        await using var scope = host.Scope();
        var organization = new IdentityOrganization { Name = "Ocharo", Slug = "ocharo" };
        (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateAsync(organization, owner)).IsSuccess.Should().BeTrue();
        return organization;
    }

    private static async Task<AdminInvitationResult> CreateAsync(AccountsHost host, AdminInvitationRequest request)
    {
        await using var scope = host.Scope();
        return await scope.ServiceProvider.GetRequiredService<AdminInvitationService>().CreateAsync(request);
    }
}

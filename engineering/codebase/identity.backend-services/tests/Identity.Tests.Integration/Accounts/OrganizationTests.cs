using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

/// <summary>Organizations, memberships, organizations left without an owner, organization claims and personal data.</summary>
public sealed class OrganizationTests
{
    private static AccountsHost CreateHost() => AccountsHost.Create(identity => identity
        .AddOrganizations(o =>
        {
            o.Roles["editor"] = ["can-edit"];
            o.Roles["viewer"] = [];
            o.Roles["owner"] = ["can-edit", "can-approve"];
        })
        .AddPersonalData());

    private static OrganizationService Organizations(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<OrganizationService>();

    [Fact]
    public async Task Create_ShouldMakeTheCreatorOwnerAndKeepHandlesUnique()
    {
        await using var host = CreateHost();
        var founder = await host.CreateUserAsync("dilnoza");
        await using var scope = host.Scope();
        var organizations = Organizations(scope);

        var ocharo = new IdentityOrganization { Name = "  Ocharo ", Slug = "Ocharo" };
        (await organizations.CreateAsync(ocharo, founder)).IsSuccess.Should().BeTrue();

        ocharo.Id.Should().NotBeEmpty();
        ocharo.Name.Should().Be("Ocharo");
        ocharo.Slug.Should().Be("ocharo");
        ocharo.CreatedAt.Should().Be(host.Time.GetUtcNow());
        (await organizations.GetRoleAsync(ocharo, founder)).Should().Be("owner");
        (await organizations.FindBySlugAsync("OCHARO"))!.Id.Should().Be(ocharo.Id);
        (await organizations.CreateAsync(new IdentityOrganization { Name = "Copy", Slug = "ocharo" }, founder))
            .Errors.Should().ContainSingle(e => e.Code == IdentityErrorCodeConstants.DuplicateOrganizationSlug);
    }

    [Theory]
    [InlineData("", "valid")]
    [InlineData("Name", "")]
    [InlineData("Name", "two--hyphens")]
    [InlineData("Name", "-leading")]
    [InlineData("Name", "spa ce")]
    [InlineData("Name", "kirill-кир")]
    public async Task Create_ShouldRejectAMissingNameOrAMalformedHandle(string name, string slug)
    {
        await using var host = CreateHost();
        var founder = await host.CreateUserAsync("bekzod");
        await using var scope = host.Scope();

        var result = await Organizations(scope).CreateAsync(new IdentityOrganization { Name = name, Slug = slug }, founder);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == (name.Length == 0
            ? IdentityErrorCodeConstants.OrganizationNameRequired
            : IdentityErrorCodeConstants.InvalidOrganizationSlug));
    }

    [Fact]
    public async Task Members_ShouldBeAddedReRoledAndRemoved()
    {
        await using var host = CreateHost();
        var founder = await host.CreateUserAsync("malika");
        var member = await host.CreateUserAsync("jasur");
        await using var scope = host.Scope();
        var organizations = Organizations(scope);
        var ocharo = new IdentityOrganization { Name = "Ocharo", Slug = "ocharo" };
        await organizations.CreateAsync(ocharo, founder);

        (await organizations.AddMemberAsync(ocharo, member, "Editor")).IsSuccess.Should().BeTrue();
        (await organizations.GetRoleAsync(ocharo, member)).Should().Be("editor");
        (await organizations.AddMemberAsync(ocharo, member, "viewer")).Errors.Should().ContainSingle(e => e.Code == IdentityErrorCodeConstants.UserAlreadyMember);
        (await organizations.AddMemberAsync(ocharo, await host.CreateUserAsync("aziz"), "operator")).Errors.Should().ContainSingle(e => e.Code == IdentityErrorCodeConstants.UnknownOrganizationRole);

        var stamp = (await host.ReloadAsync(member.Id)).SecurityStamp;
        (await organizations.ChangeRoleAsync(ocharo, member, "viewer")).IsSuccess.Should().BeTrue();
        (await organizations.GetRoleAsync(ocharo, member)).Should().Be("viewer");
        var changed = (await host.ReloadAsync(member.Id)).SecurityStamp;
        changed.Should().NotBe(stamp);

        (await organizations.RemoveMemberAsync(ocharo, member)).IsSuccess.Should().BeTrue();
        (await organizations.GetRoleAsync(ocharo, member)).Should().BeNull();
        (await host.ReloadAsync(member.Id)).SecurityStamp.Should().NotBe(changed);
        (await organizations.RemoveMemberAsync(ocharo, member)).Errors.Should().ContainSingle(e => e.Code == IdentityErrorCodeConstants.UserNotMember);
        (await organizations.ListMembersAsync(ocharo)).Should().ContainSingle().Which.UserId.Should().Be(founder.Id);
    }

    [Fact]
    public async Task LastOwner_ShouldBeDemotedOrRemoved_LeavingNoOwner()
    {
        await using var host = CreateHost();
        var founder = await host.CreateUserAsync("nodira");
        var partner = await host.CreateUserAsync("sardor");
        await using var scope = host.Scope();
        var organizations = Organizations(scope);
        var ocharo = new IdentityOrganization { Name = "Ocharo", Slug = "ocharo" };
        await organizations.CreateAsync(ocharo, founder);
        await organizations.AddMemberAsync(ocharo, partner, "admin");

        (await organizations.ChangeRoleAsync(ocharo, founder, "editor")).IsSuccess.Should().BeTrue();
        (await organizations.ListMembersAsync(ocharo)).Should().NotContain(m => m.Role == "owner", "demoting the last owner leaves none");

        (await organizations.ChangeRoleAsync(ocharo, partner, "owner")).IsSuccess.Should().BeTrue();
        (await organizations.RemoveMemberAsync(ocharo, partner)).IsSuccess.Should().BeTrue();
        (await organizations.ListMembersAsync(ocharo)).Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { UserId = founder.Id, Role = "editor" }, "removing the last owner leaves none");
    }

    [Fact]
    public async Task LastOwnersAccountDeletion_ShouldLeaveTheOrganizationWithoutAnOwner()
    {
        await using var host = CreateHost();
        var founder = await host.CreateUserAsync("kamola");
        var member = await host.CreateUserAsync("rustam");
        await using var scope = host.Scope();
        var organizations = Organizations(scope);
        var ocharo = new IdentityOrganization { Name = "Ocharo", Slug = "ocharo" };
        await organizations.CreateAsync(ocharo, founder);
        await organizations.AddMemberAsync(ocharo, member, "admin");

        await scope.ServiceProvider.GetRequiredService<UserPersonalDataService>().DeleteAsync(await host.ReloadAsync(founder.Id));

        (await organizations.ListMembersAsync(ocharo)).Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { UserId = member.Id, Role = "admin" });
    }

    [Fact]
    public async Task Principal_ShouldCarryRoleAndPermissionsPerOrganization()
    {
        await using var host = CreateHost();
        var founder = await host.CreateUserAsync("lola");
        var member = await host.CreateUserAsync("timur");
        await using var scope = host.Scope();
        var organizations = Organizations(scope);
        var ocharo = new IdentityOrganization { Name = "Ocharo", Slug = "ocharo" };
        var atelier = new IdentityOrganization { Name = "Atelier", Slug = "atelier" };
        await organizations.CreateAsync(ocharo, founder);
        await organizations.CreateAsync(atelier, member);
        await organizations.AddMemberAsync(ocharo, member, "editor");

        var factory = scope.ServiceProvider.GetRequiredService<UserClaimsPrincipalFactory>();
        var principal = await factory.CreateAsync(await host.ReloadAsync(member.Id));

        principal.GetOrganizationRole(ocharo.Id).Should().Be("editor");
        principal.GetOrganizationRole(atelier.Id).Should().Be("owner");
        principal.HasOrganizationPermission(ocharo.Id, "can-edit").Should().BeTrue();
        principal.HasOrganizationPermission(ocharo.Id, "can-approve").Should().BeFalse();
        principal.HasOrganizationPermission(atelier.Id, "can-approve").Should().BeTrue();
        principal.GetOrganizationRole(Guid.NewGuid()).Should().BeNull();
    }

    [Fact]
    public async Task Delete_ShouldRemoveMembershipsAndRotateEveryMembersStamp()
    {
        await using var host = CreateHost();
        var founder = await host.CreateUserAsync("shahzoda");
        var member = await host.CreateUserAsync("otabek");
        await using var scope = host.Scope();
        var organizations = Organizations(scope);
        var ocharo = new IdentityOrganization { Name = "Ocharo", Slug = "ocharo" };
        await organizations.CreateAsync(ocharo, founder);
        await organizations.AddMemberAsync(ocharo, member, "viewer");
        var stamp = (await host.ReloadAsync(member.Id)).SecurityStamp;

        (await organizations.DeleteAsync(ocharo)).IsSuccess.Should().BeTrue();

        (await organizations.FindByIdAsync(ocharo.Id)).Should().BeNull();
        (await organizations.ListForUserAsync(member)).Should().BeEmpty();
        (await host.ReloadAsync(member.Id)).SecurityStamp.Should().NotBe(stamp);
    }

    [Fact]
    public async Task PersonalData_ShouldExportAndEraseMemberships()
    {
        await using var host = CreateHost();
        var founder = await host.CreateUserAsync("gulnora");
        var member = await host.CreateUserAsync("ulugbek");
        await using var scope = host.Scope();
        var organizations = Organizations(scope);
        var ocharo = new IdentityOrganization { Name = "Ocharo", Slug = "ocharo" };
        await organizations.CreateAsync(ocharo, founder);
        await organizations.AddMemberAsync(ocharo, member, "editor");
        var personalData = scope.ServiceProvider.GetRequiredService<UserPersonalDataService>();

        var export = await personalData.ExportAsync(await host.ReloadAsync(member.Id));
        export.Sections.Should().ContainKey("organizations");

        await personalData.DeleteAsync(await host.ReloadAsync(member.Id));

        (await organizations.ListMembersAsync(ocharo)).Should().ContainSingle().Which.UserId.Should().Be(founder.Id);
    }
}

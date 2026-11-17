using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Identity.Members;

/// <summary>
/// The member broker's conformance cases, run against every implementation: the in-process Module, the Client over the
/// service, and <see cref="FakeMemberBroker"/>. A subclass builds the broker and arranges the organizations and people.
/// </summary>
public abstract class MemberBrokerConformance
{
    /// <summary>The broker under test, reading as one app.</summary>
    protected abstract IMemberBroker Broker { get; }

    /// <summary>Creates an organization whose first member, an owner linked to the bot, joins with it; returns its id and the owner's <c>sub</c>.</summary>
    protected abstract Task<(Guid OrganizationId, string OwnerSub)> CreateOrganizationAsync();

    /// <summary>Adds a new person to the organization with <paramref name="role"/>, linked to the bot or not, and returns their <c>sub</c>.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="role">A catalog role.</param>
    /// <param name="isLinked">Whether the person's confirmed phone is linked to the bot.</param>
    protected abstract Task<string> AddMemberAsync(Guid organizationId, string role, bool isLinked);

    /// <summary>Every member reads, earliest joined first, with their role and whether the bot reaches them.</summary>
    [Fact]
    public async Task List_ShouldReadEveryMember_WithRoleAndTelegramFlag()
    {
        var (organization, owner) = await CreateOrganizationAsync();
        var editor = await AddMemberAsync(organization, "editor", isLinked: true);
        var viewer = await AddMemberAsync(organization, "viewer", isLinked: false);

        var members = Listed(await Broker.ListAsync(organization));

        Assert.Equal([(owner, "owner", true), (editor, "editor", true), (viewer, "viewer", false)], members.Select(m => (m.Subject, m.Role, m.HasTelegramLink)));
    }

    /// <summary>A role reads only its members, in any case; another organization's members never show.</summary>
    [Fact]
    public async Task List_ShouldReadOnlyTheRolesMembers_WhenARoleIsNamed()
    {
        var (organization, _) = await CreateOrganizationAsync();
        var first = await AddMemberAsync(organization, "editor", isLinked: true);
        await AddMemberAsync(organization, "viewer", isLinked: true);
        var second = await AddMemberAsync(organization, "editor", isLinked: false);
        var (other, _) = await CreateOrganizationAsync();
        await AddMemberAsync(other, "editor", isLinked: true);

        var editors = Listed(await Broker.ListAsync(organization, "Editor"));

        Assert.Equal([first, second], editors.Select(member => member.Subject));
        Assert.All(editors, member => Assert.Equal("editor", member.Role));
    }

    /// <summary>An organization the instance lacks reads <c>OrganizationNotFound</c>.</summary>
    [Fact]
    public async Task List_ShouldFailOrganizationNotFound_ForAnUnknownOrganization()
    {
        await CreateOrganizationAsync();

        Assert.Equal("OrganizationNotFound", CodeOf(await Broker.ListAsync(Guid.NewGuid())));
    }

    /// <summary>A role outside the catalog reads <c>UnknownOrganizationRole</c>.</summary>
    [Fact]
    public async Task List_ShouldFailUnknownOrganizationRole_ForARoleOutsideTheCatalog()
    {
        var (organization, _) = await CreateOrganizationAsync();

        Assert.Equal("UnknownOrganizationRole", CodeOf(await Broker.ListAsync(organization, "member")));
    }

    private static IReadOnlyList<MemberModel> Listed(Result<IReadOnlyList<MemberModel>> result)
    {
        if (result.IsFailure(out var error, out var members) || members is null)
            Assert.Fail($"The broker failed: {error?.Message}");
        return members!;
    }

    private static string? CodeOf(Result<IReadOnlyList<MemberModel>> result)
    {
        if (result.IsFailure(out var error, out _) && error is not null)
            return error.Metadata?.TryGetValue(MemberConstants.CodeMetadataKey, out var code) == true ? code as string : null;

        Assert.Fail("The broker should have failed.");
        return null;
    }
}

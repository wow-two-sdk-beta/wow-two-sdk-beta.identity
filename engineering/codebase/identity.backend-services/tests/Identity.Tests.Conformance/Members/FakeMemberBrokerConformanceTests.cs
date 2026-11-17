using WoW.Two.Sdk.Beta.Be.Identity.Members;

namespace Identity.Tests.Conformance.Members;

/// <summary>The member cases against the test kit's <see cref="FakeMemberBroker"/>, so a consumer's tests stand on the contract.</summary>
public sealed class FakeMemberBrokerConformanceTests : MemberBrokerConformance
{
    private readonly FakeMemberBroker _fake = new();

    /// <inheritdoc />
    protected override IMemberBroker Broker => _fake;

    /// <inheritdoc />
    protected override Task<(Guid OrganizationId, string OwnerSub)> CreateOrganizationAsync()
    {
        var organization = Guid.NewGuid();
        var owner = Guid.NewGuid().ToString();
        _fake.AddMember(organization, owner, "owner", hasTelegramLink: true);
        return Task.FromResult((organization, owner));
    }

    /// <inheritdoc />
    protected override Task<string> AddMemberAsync(Guid organizationId, string role, bool isLinked)
    {
        var sub = Guid.NewGuid().ToString();
        _fake.AddMember(organizationId, sub, role, isLinked);
        return Task.FromResult(sub);
    }
}

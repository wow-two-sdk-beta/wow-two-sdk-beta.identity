using WoW.Two.Sdk.Beta.Be.Identity.Notices;

namespace Identity.Tests.Conformance.Notices;

/// <summary>The app notice cases against the test kit's <see cref="FakeAppNoticeBroker"/>, so a consumer's tests stand on the contract.</summary>
public sealed class FakeAppNoticeBrokerConformanceTests : AppNoticeBrokerConformance
{
    private readonly FakeAppNoticeBroker _fake = new();

    /// <inheritdoc />
    protected override IAppNoticeBroker Broker => _fake;

    /// <inheritdoc />
    protected override Task<string> CreateLinkedPersonAsync()
    {
        var sub = Guid.NewGuid().ToString();
        _fake.Link(sub);
        return Task.FromResult(sub);
    }

    /// <inheritdoc />
    protected override Task<string> CreateUnlinkedPersonAsync() => Task.FromResult(Guid.NewGuid().ToString());

    /// <inheritdoc />
    protected override Task MuteAsync(string sub)
    {
        _fake.Mute(sub);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task<int> CountDeliveredAsync(string sub) => Task.FromResult(_fake.Sent.Count(notice => notice.Subject == sub));
}

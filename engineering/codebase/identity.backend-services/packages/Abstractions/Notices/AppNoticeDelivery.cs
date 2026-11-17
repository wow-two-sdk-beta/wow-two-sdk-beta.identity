namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>What became of an app notice that Identity took (<see cref="IAppNoticeBroker"/>), and the body of the route's answer.</summary>
public sealed record AppNoticeDelivery
{
    /// <summary>The notice's id in the bot's message ledger when <see cref="Status"/> is <see cref="AppNoticeStatusConstants.Sent"/>; null otherwise.</summary>
    public Guid? Id { get; init; }

    /// <summary><see cref="AppNoticeStatusConstants.Sent"/>, <see cref="AppNoticeStatusConstants.Muted"/> or <see cref="AppNoticeStatusConstants.NotLinked"/>.</summary>
    public required string Status { get; init; }
}

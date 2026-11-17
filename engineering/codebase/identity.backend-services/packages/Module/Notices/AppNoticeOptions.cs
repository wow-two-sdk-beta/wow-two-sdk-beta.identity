namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>Holds the limits of app notices, and the app name an in-process host sends as.</summary>
/// <remarks>
/// Set in code with <c>.AddAppNotices(o => …)</c> or in the host section <c>Identity:Notices</c>, applied last. The rate
/// limit is read once, at the first notice; the rest live.
/// </remarks>
public sealed record AppNoticeOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Identity:Notices";

    /// <summary>The longest <see cref="LocalClientId"/>, and any caller's client id.</summary>
    public const int MaxClientIdLength = 256;

    /// <summary>
    /// Gets or sets the app name the in-process broker (<see cref="TelegramAppNoticeBroker"/>) sends as, for a Form 1 host:
    /// its idempotency keys and the people's mutes are kept under it. The service sends as each caller's client id. Default
    /// <c>app</c>.
    /// </summary>
    public string LocalClientId { get; set; } = "app";

    /// <summary>Gets or sets how many notices one app may send per <see cref="Window"/>, muted and unlinked ones included. Default 600.</summary>
    public int PermitLimit { get; set; } = 600;

    /// <summary>Gets or sets the sliding window <see cref="PermitLimit"/> counts within. Default one minute.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Gets or sets how long an idempotency key is remembered after its first use. Default 7 days.</summary>
    public TimeSpan IdempotencyWindow { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Gets or sets the longest a notice waits for its turn under Telegram's pace (25 a second for the bot, 1 a second per
    /// chat) before it is refused as rate limited. Default 5 seconds.
    /// </summary>
    public TimeSpan MaxPaceWait { get; set; } = TimeSpan.FromSeconds(5);
}

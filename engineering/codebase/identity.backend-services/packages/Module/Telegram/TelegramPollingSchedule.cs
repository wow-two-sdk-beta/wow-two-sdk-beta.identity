namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>Holds the pacing of the bot's long polling: how long Telegram holds a poll open, how many updates one poll takes, and the wait after a failed poll.</summary>
internal sealed record TelegramPollingSchedule
{
    /// <summary>The poll Telegram holds open until an update arrives, in seconds.</summary>
    public int PollTimeoutSeconds { get; init; } = 30;

    /// <summary>The most updates one poll takes.</summary>
    public int BatchLimit { get; init; } = 100;

    /// <summary>The wait after the first failed poll; each further failure doubles it up to <see cref="MaxBackoff"/>.</summary>
    public TimeSpan MinBackoff { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The longest wait between two polls after failures.</summary>
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>The wait between two looks at a bot that is switched off while the service runs.</summary>
    public TimeSpan IdleDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The wait after <paramref name="failures"/> failed polls in a row, at least one.</summary>
    /// <param name="failures">The failures so far.</param>
    public TimeSpan BackoffAfter(int failures)
    {
        var doublings = Math.Clamp(failures - 1, 0, 20);
        var ticks = MinBackoff.Ticks * (1L << doublings);
        return TimeSpan.FromTicks(Math.Min(ticks < 0 ? MaxBackoff.Ticks : ticks, MaxBackoff.Ticks));
    }
}

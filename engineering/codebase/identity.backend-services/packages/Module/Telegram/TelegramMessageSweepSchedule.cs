namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>Holds the pacing of the bot's message sweep: how often it runs and how many due messages one run takes.</summary>
public sealed record TelegramMessageSweepSchedule
{
    /// <summary>The wait between two sweeps. Default 1 minute.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>The most due messages one sweep takes, longest due first; the rest wait for the next. Default 500.</summary>
    public int BatchLimit { get; init; } = 500;
}

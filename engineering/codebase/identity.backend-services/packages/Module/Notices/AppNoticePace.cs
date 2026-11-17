using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>
/// Keeps app notices inside each app's rate limit (<see cref="AppNoticeOptions.PermitLimit"/> per
/// <see cref="AppNoticeOptions.Window"/>, sliding) and inside Telegram's pace: <see cref="BotMessagesPerSecond"/> for the
/// bot, under Telegram's 30, which leaves room for codes and the bot's own notices, and one message a second per chat.
/// One per instance: replicas each keep their own.
/// </summary>
public sealed class AppNoticePace : IDisposable
{
    /// <summary>The app notices the bot sends per second at most.</summary>
    public const int BotMessagesPerSecond = 25;

    /// <summary>The least time between two app notices to one chat.</summary>
    public static readonly TimeSpan ChatInterval = TimeSpan.FromSeconds(1);

    private const int PrunedChatCount = 10_000;
    private static readonly long BotIntervalTicks = TimeSpan.TicksPerSecond / BotMessagesPerSecond;

    private readonly object _gate = new();
    private readonly Dictionary<long, long> _chatNext = [];
    private readonly PartitionedRateLimiter<string> _apps;
    private readonly TimeSpan _segment;
    private readonly TimeProvider _time;
    private long _botNext;

    /// <summary>Creates the pace on the system clock: Telegram counts in real time, whatever clock the rest of the instance reads.</summary>
    /// <param name="options">The limits.</param>
    public AppNoticePace(IOptions<AppNoticeOptions> options)
        : this(options, TimeProvider.System)
    {
    }

    /// <summary>Creates the pace on <paramref name="time"/>.</summary>
    /// <param name="options">The limits.</param>
    /// <param name="time">The clock the pace counts by.</param>
    internal AppNoticePace(IOptions<AppNoticeOptions> options, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        var limits = options.Value;
        _time = time;
        _segment = limits.Window / 6;
        _apps = PartitionedRateLimiter.Create<string, string>(app => RateLimitPartition.GetSlidingWindowLimiter(app, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = limits.PermitLimit,
            Window = limits.Window,
            SegmentsPerWindow = 6,
            QueueLimit = 0,
        }));
    }

    /// <summary>Counts one notice against <paramref name="clientId"/>'s rate limit.</summary>
    /// <param name="clientId">The app.</param>
    /// <param name="retryAfter">How long to wait before a retry, when refused.</param>
    /// <returns>Whether the app is inside its limit.</returns>
    public bool TryAdmit(string clientId, out TimeSpan retryAfter)
    {
        using var lease = _apps.AttemptAcquire(clientId);
        retryAfter = lease.IsAcquired ? TimeSpan.Zero : lease.TryGetMetadata(MetadataName.RetryAfter, out var after) ? after : _segment;
        return lease.IsAcquired;
    }

    /// <summary>
    /// Takes the next turn to send to <paramref name="chatId"/> and waits for it, unless it is further off than
    /// <paramref name="maxWait"/>.
    /// </summary>
    /// <param name="chatId">The chat.</param>
    /// <param name="maxWait">The longest wait accepted.</param>
    /// <param name="cancellationToken">Token to cancel the wait.</param>
    /// <returns>Null once the turn came; otherwise how long until a retry may get one, with no turn taken.</returns>
    public async Task<TimeSpan?> WaitTurnAsync(long chatId, TimeSpan maxWait, CancellationToken cancellationToken = default)
    {
        var wait = Reserve(chatId, maxWait);
        if (wait > maxWait)
            return wait;

        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, _time, cancellationToken);
        return null;
    }

    /// <inheritdoc />
    public void Dispose() => _apps.Dispose();

    /// <summary>The wait until the chat's turn; the turn is taken only when the wait is within <paramref name="maxWait"/>.</summary>
    private TimeSpan Reserve(long chatId, TimeSpan maxWait)
    {
        var now = _time.GetUtcNow().UtcTicks;
        lock (_gate)
        {
            var start = Math.Max(now, _botNext);
            if (_chatNext.TryGetValue(chatId, out var chatNext))
                start = Math.Max(start, chatNext);

            var wait = TimeSpan.FromTicks(start - now);
            if (wait > maxWait)
                return wait;

            _botNext = start + BotIntervalTicks;
            if (_chatNext.Count >= PrunedChatCount)
            {
                foreach (var idle in _chatNext.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToList())
                    _chatNext.Remove(idle);
            }

            _chatNext[chatId] = start + ChatInterval.Ticks;
            return wait;
        }
    }
}

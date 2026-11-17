using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>
/// Stands in for Identity's app notices with no instance and no bot: people are linked or muted by the test, notices to
/// linked people land in <see cref="Sent"/>, and the contract's rules hold, the idempotency key's included. It checks the
/// body's shape, never Telegram's HTML.
/// </summary>
public sealed class FakeAppNoticeBroker : IAppNoticeBroker
{
    private readonly object _gate = new();
    private readonly HashSet<string> _linked = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _muted = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Body, AppNoticeDelivery Delivery)> _keys = new(StringComparer.Ordinal);
    private readonly List<AppNotice> _sent = [];
    private readonly Queue<AppError> _failures = new();

    /// <summary>The notices that reached a person, in order.</summary>
    public IReadOnlyList<AppNotice> Sent
    {
        get
        {
            lock (_gate)
                return [.. _sent];
        }
    }

    /// <summary>Gives <paramref name="sub"/> a Telegram link, so notices reach them.</summary>
    /// <param name="sub">The person's <c>sub</c>.</param>
    public FakeAppNoticeBroker Link(string sub)
    {
        lock (_gate)
            _linked.Add(sub);
        return this;
    }

    /// <summary>Mutes the app's notices for <paramref name="sub"/>, as the notice's mute button or <c>/stop</c> does.</summary>
    /// <param name="sub">The person's <c>sub</c>.</param>
    public FakeAppNoticeBroker Mute(string sub)
    {
        lock (_gate)
            _muted.Add(sub);
        return this;
    }

    /// <summary>Lifts <paramref name="sub"/>'s mute, as a bare <c>/start</c> does.</summary>
    /// <param name="sub">The person's <c>sub</c>.</param>
    public FakeAppNoticeBroker Unmute(string sub)
    {
        lock (_gate)
            _muted.Remove(sub);
        return this;
    }

    /// <summary>Makes the next send fail with <paramref name="error"/>, such as a rate limit (<see cref="AppNoticeErrorCodeConstants.RateLimited"/>).</summary>
    /// <param name="error">The failure.</param>
    public FakeAppNoticeBroker FailNext(AppError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        lock (_gate)
            _failures.Enqueue(error);
        return this;
    }

    /// <inheritdoc />
    public Task<Result<AppNoticeDelivery>> SendNoticeAsync(AppNotice notice, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notice);
        lock (_gate)
            return Task.FromResult(Send(notice));
    }

    private Result<AppNoticeDelivery> Send(AppNotice notice)
    {
        if (_failures.TryDequeue(out var failure))
            return Result<AppNoticeDelivery>.Fail(failure);
        if (ProblemOf(notice) is { } problem)
            return Fail(AppErrorType.Validation, problem, AppNoticeErrorCodeConstants.Invalid);

        var body = JsonSerializer.Serialize(new { notice.Subject, notice.Text, Buttons = (notice.Buttons ?? []).Select(b => new[] { b.Text, b.Url }), notice.Culture });
        if (_keys.TryGetValue(notice.IdempotencyKey, out var first))
        {
            return first.Body == body
                ? Result<AppNoticeDelivery>.Ok(first.Delivery)
                : Fail(AppErrorType.Conflict, "The idempotency key was used before with another notice.", AppNoticeErrorCodeConstants.IdempotencyKeyReused);
        }

        if (!_linked.Contains(notice.Subject))
            return Result<AppNoticeDelivery>.Ok(new AppNoticeDelivery { Status = AppNoticeStatusConstants.NotLinked });

        var delivery = _muted.Contains(notice.Subject)
            ? new AppNoticeDelivery { Status = AppNoticeStatusConstants.Muted }
            : new AppNoticeDelivery { Status = AppNoticeStatusConstants.Sent, Id = Guid.CreateVersion7() };
        if (delivery.Status == AppNoticeStatusConstants.Sent)
            _sent.Add(notice);
        _keys[notice.IdempotencyKey] = (body, delivery);
        return Result<AppNoticeDelivery>.Ok(delivery);
    }

    private static string? ProblemOf(AppNotice notice)
    {
        if (!Guid.TryParse(notice.Subject, out _))
            return "sub must be a person's id, a uuid.";
        if (string.IsNullOrWhiteSpace(notice.Text) || notice.Text.Length > AppNoticeConstants.MaxTextLength)
            return $"text needs 1–{AppNoticeConstants.MaxTextLength} characters.";
        if (string.IsNullOrEmpty(notice.IdempotencyKey) || notice.IdempotencyKey.Length > AppNoticeConstants.MaxIdempotencyKeyLength
            || notice.IdempotencyKey.Any(c => c is < '!' or > '~'))
            return "idempotencyKey must be printable ASCII with no spaces.";
        var buttons = notice.Buttons ?? [];
        if (buttons.Count > AppNoticeConstants.MaxButtons)
            return $"A notice carries at most {AppNoticeConstants.MaxButtons} buttons.";
        return buttons.Any(b => b is null || string.IsNullOrWhiteSpace(b.Text) || b.Text.Length > AppNoticeConstants.MaxButtonTextLength
                || !Uri.TryCreate(b.Url, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
            ? "Each button needs text and an absolute https or http url."
            : null;
    }

    private static Result<AppNoticeDelivery> Fail(AppErrorType type, string message, string code)
        => Result<AppNoticeDelivery>.Fail(AppError.Of(type, message, new Dictionary<string, object?>(StringComparer.Ordinal) { [AppNoticeConstants.CodeMetadataKey] = code }));
}

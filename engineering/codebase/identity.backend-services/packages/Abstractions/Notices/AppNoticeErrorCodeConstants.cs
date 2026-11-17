namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>
/// Names the app notice codes a problem carries in its <c>identityCode</c> extension, and a broker failure in its
/// <c>identityCode</c> metadata (<see cref="AppNoticeConstants.CodeMetadataKey"/>).
/// </summary>
public static class AppNoticeErrorCodeConstants
{
    /// <summary><c>400</c>: the body breaks a rule (the <c>sub</c>, the text or its HTML, a button, the key, the culture).</summary>
    public const string Invalid = "NoticeInvalid";

    /// <summary><c>404</c>: the person has no confirmed phone linked to the bot; a success reading <see cref="AppNoticeStatusConstants.NotLinked"/> through the broker.</summary>
    public const string NotLinked = "NoticeNotLinked";

    /// <summary><c>409</c>: the app used the idempotency key before with another body.</summary>
    public const string IdempotencyKeyReused = "IdempotencyKeyReused";

    /// <summary><c>409</c>: a request with the same key is being sent now; retry after <c>Retry-After</c>.</summary>
    public const string InProgress = "NoticeInProgress";

    /// <summary><c>429</c>: past the app's rate limit, or past Telegram's pace for the bot or the chat; retry after <c>Retry-After</c>.</summary>
    public const string RateLimited = "NoticeRateLimited";

    /// <summary><c>502</c>: Telegram refused the message, such as a person who blocked the bot; the problem's <c>reason</c> says why.</summary>
    public const string Refused = "NoticeRefused";

    /// <summary><c>503</c>: the bot is off, or Telegram could not be reached; retry after <c>Retry-After</c>.</summary>
    public const string Unavailable = "NoticeUnavailable";
}

namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>Holds the app notice contract's route, scope and limits.</summary>
public static class AppNoticeConstants
{
    /// <summary>The service-to-service route a Form 2 or 3 app posts an <see cref="AppNotice"/> to.</summary>
    public const string Path = "/api/identity/v1/notices";

    /// <summary>The scope a client-credentials token needs on the route, granted per client in the instance's declaration.</summary>
    public const string Scope = "identity.notices.send";

    /// <summary>The most characters <see cref="AppNotice.Text"/> holds as sent, tags and entities included: Telegram's message limit.</summary>
    public const int MaxTextLength = 4096;

    /// <summary>The most link buttons a notice carries.</summary>
    public const int MaxButtons = 8;

    /// <summary>The most characters a button's label holds.</summary>
    public const int MaxButtonTextLength = 64;

    /// <summary>The most characters a button's address holds.</summary>
    public const int MaxUrlLength = 2048;

    /// <summary>The most characters an idempotency key holds.</summary>
    public const int MaxIdempotencyKeyLength = 128;

    /// <summary>The error metadata key holding the code (<see cref="AppNoticeErrorCodeConstants"/>), the problem's extension of the same name.</summary>
    public const string CodeMetadataKey = "identityCode";

    /// <summary>The error metadata key holding the whole seconds to wait before a retry, the <c>Retry-After</c> header's value.</summary>
    public const string RetryAfterMetadataKey = "retryAfter";

    /// <summary>The error metadata key, and problem extension, holding Telegram's refusal for <see cref="AppNoticeErrorCodeConstants.Refused"/>.</summary>
    public const string ReasonMetadataKey = "reason";
}

using System.Globalization;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Factories;

namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>
/// Makes app notice failures: the code in <c>identityCode</c> (metadata and problem extension), the whole seconds to wait in
/// <c>retryAfter</c>, and Telegram's refusal in <c>reason</c>.
/// </summary>
internal static class AppNoticeErrors
{
    public static AppError Invalid(string message) => Of(AppErrorType.Validation, message, AppNoticeErrorCodeConstants.Invalid);

    public static AppError KeyReused()
        => Of(AppErrorType.Conflict, "The idempotency key was used before with another notice.", AppNoticeErrorCodeConstants.IdempotencyKeyReused);

    public static AppError InProgress()
        => Of(AppErrorType.Conflict, "A notice with this idempotency key is being sent now.", AppNoticeErrorCodeConstants.InProgress, TimeSpan.FromSeconds(1));

    public static AppError RateLimited(TimeSpan retryAfter)
        => Of(AppErrorType.TooManyRequests, "Too many notices; retry later.", AppNoticeErrorCodeConstants.RateLimited, retryAfter);

    public static AppError Refused(string reason)
        => Of(AppErrorType.ExternalUnavailable, "Telegram refused the notice.", AppNoticeErrorCodeConstants.Refused, reason: reason);

    public static AppError Unavailable(string message, TimeSpan retryAfter)
        => Of(AppErrorType.ExternalUnavailable, message, AppNoticeErrorCodeConstants.Unavailable, retryAfter);

    private static AppError Of(AppErrorType type, string message, string code, TimeSpan? retryAfter = null, string? reason = null)
    {
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { [AppNoticeConstants.CodeMetadataKey] = code };
        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["messageKey"] = code,
            [AppNoticeConstants.CodeMetadataKey] = code,
            [AppErrorProblemDetailsFactory.ExtensionsMetadataKey] = extensions,
        };
        if (retryAfter is { } wait)
            metadata[AppNoticeConstants.RetryAfterMetadataKey] = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        if (reason is not null)
            metadata[AppNoticeConstants.ReasonMetadataKey] = extensions[AppNoticeConstants.ReasonMetadataKey] = reason;
        return AppError.Of(type, message, metadata);
    }
}

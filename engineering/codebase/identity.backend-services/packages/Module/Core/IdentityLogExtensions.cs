using Microsoft.Extensions.Logging;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>Writes the identity slices' source-generated log events (IDs 4101–4199).</summary>
internal static partial class IdentityLogExtensions
{
    [LoggerMessage(EventId = 4101, Level = LogLevel.Warning, Message = "The breached-password corpus could not be queried; the check {Outcome}.")]
    internal static partial void BreachCheckUnavailable(this ILogger logger, Exception exception, string outcome);

    [LoggerMessage(EventId = 4111, Level = LogLevel.Information, Message = "A principal for user {UserId} carried a stale security stamp and was rejected.")]
    internal static partial void SecurityStampRejected(this ILogger logger, string userId);

    [LoggerMessage(EventId = 4131, Level = LogLevel.Warning, Message = "A consumed refresh token was presented again; family {FamilyId} was revoked.")]
    internal static partial void RefreshTokenReused(this ILogger logger, Guid familyId);

    [LoggerMessage(EventId = 4141, Level = LogLevel.Warning, Message = "Account email skipped: {Reason}.")]
    internal static partial void AccountEmailSkipped(this ILogger logger, string reason);

    [LoggerMessage(EventId = 4142, Level = LogLevel.Warning, Message = "Account email failed: {Reason}")]
    internal static partial void AccountEmailFailed(this ILogger logger, string? reason);

    [LoggerMessage(EventId = 4143, Level = LogLevel.Warning, Message = "The {Channel} channel refused a password-change code or link: {Reason}")]
    internal static partial void PasswordChangeDeliveryFailed(this ILogger logger, string channel, string? reason);

    [LoggerMessage(EventId = 4144, Level = LogLevel.Warning, Message = "SMTP is not configured (Identity:Email:Smtp:Host is empty); an email was not sent.")]
    internal static partial void SmtpNotConfigured(this ILogger logger);

    [LoggerMessage(EventId = 4121, Level = LogLevel.Warning, Message = "User {UserId} was locked out until {LockoutEnd}.")]
    internal static partial void UserLockedOut(this ILogger logger, string userId, DateTimeOffset lockoutEnd);
}

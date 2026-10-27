using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;

/// <summary>
/// Records every sign-in attempt in <c>identity_sign_in_log</c>: when, the account when known, the masked login, the step,
/// the client, how it ended and why, the masked address, the user agent, the session and the trace. <c>SignInService</c>
/// records each attempt it judges; a host records the ones it refuses before (an invalid email link).
/// </summary>
/// <remarks>A failed write is logged and swallowed: the log never blocks a sign-in.</remarks>
/// <param name="repository">The log store.</param>
/// <param name="options">The retention.</param>
/// <param name="time">The clock entries take their instant from.</param>
/// <param name="logger">Receives a failed write.</param>
/// <param name="http">The current request, for the address and user agent; null outside a request.</param>
public sealed partial class SignInLogService(
    ISignInLogRepository repository,
    SignInLogOptions options,
    TimeProvider time,
    ILogger<SignInLogService> logger,
    IHttpContextAccessor? http = null)
{
    /// <summary>The longest client id kept.</summary>
    private const int MaxClientIdLength = 100;

    /// <summary>Gets or sets the OIDC client the current request's sign-in serves, as the hosted pages name it; recorded with each attempt of this scope.</summary>
    public string? ClientId { get; set; }

    /// <summary>Record one attempt.</summary>
    /// <param name="attempt">The attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The entry's id, or null when the write failed.</returns>
    public async Task<long?> RecordAsync(SignInAttempt attempt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        var (ipMasked, userAgent) = PersonalDataMasks.Describe(http?.HttpContext);
        var entry = new IdentitySignInLogEntry
        {
            OccurredAt = time.GetUtcNow(),
            UserId = attempt.UserId,
            SubjectMasked = PersonalDataMasks.MaskSubject(attempt.Subject),
            Method = attempt.Method,
            ClientId = string.IsNullOrWhiteSpace(ClientId) ? null : ClientId.Length <= MaxClientIdLength ? ClientId : ClientId[..MaxClientIdLength],
            Result = attempt.Result,
            Reason = attempt.Reason,
            IpMasked = ipMasked,
            UserAgent = userAgent,
            TraceId = PersonalDataMasks.TraceId(),
        };

        try
        {
            return await repository.AddAsync(entry, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogWriteFailed(logger, exception, attempt.Method, attempt.Result);
            return null;
        }
    }

    /// <summary>Record the session a logged successful attempt started.</summary>
    /// <param name="entryId">The entry <see cref="RecordAsync"/> returned.</param>
    /// <param name="sessionId">The session id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task AttachSessionAsync(long entryId, Guid sessionId, CancellationToken cancellationToken = default)
        => repository.SetSessionAsync(entryId, sessionId, cancellationToken);

    /// <summary>The person's latest attempts, newest first.</summary>
    /// <param name="userId">The person.</param>
    /// <param name="take">How many at most; default 50.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<IReadOnlyList<IdentitySignInLogEntry>> ListForUserAsync(Guid userId, int take = 50, CancellationToken cancellationToken = default)
        => repository.ListForUserAsync(userId, take, cancellationToken);

    /// <summary>Delete the entries older than <see cref="SignInLogOptions.RetentionDays"/>; safe to run on several replicas at once.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many entries were deleted.</returns>
    public Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default)
        => repository.DeleteBeforeAsync(time.GetUtcNow() - TimeSpan.FromDays(options.RetentionDays), cancellationToken);

    [LoggerMessage(EventId = 7301, Level = LogLevel.Error, Message = "The sign-in log could not record a {Method} attempt ({Result})")]
    private static partial void LogWriteFailed(ILogger logger, Exception exception, string method, string result);
}

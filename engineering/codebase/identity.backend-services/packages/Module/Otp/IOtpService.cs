namespace WoW.Two.Sdk.Beta.Be.Identity.Otp;

/// <summary>Defines behavior that orchestrates one-time-password creation and verification keyed by <c>(subject, scope)</c>; the subject is any consumer-defined key and the caller owns delivery (see <see cref="IOtpDeliveryHandler"/>).</summary>
public interface IOtpService
{
    /// <summary>Generates and stores a new code for <c>(subject, scope)</c>, returning it for delivery; fails with <see cref="OtpFailureReason.RateLimited"/> when one was created within the configured window.</summary>
    /// <param name="subject">Consumer-defined identity key (phone, email, username).</param>
    /// <param name="scope">Consumer-defined scope the code is valid for (e.g. <c>"crm"</c>, <c>"login"</c>).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<OtpCreationResult> CreateAsync(string subject, string scope, CancellationToken cancellationToken = default);

    /// <summary>Generates and stores a new code shaped by <paramref name="spec"/>; otherwise as <see cref="CreateAsync(string, string, CancellationToken)"/>.</summary>
    /// <param name="subject">Consumer-defined identity key (phone, email, user id).</param>
    /// <param name="scope">Consumer-defined scope the code is valid for.</param>
    /// <param name="spec">The code's characters, length and lifetime; a null lifetime takes the configured one.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<OtpCreationResult> CreateAsync(string subject, string scope, OtpCodeSpec spec, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates and stores a new code shaped by <paramref name="spec"/>, rate-limited by its subject and by
    /// <paramref name="origin"/> as well: one code per window for each.
    /// </summary>
    /// <param name="subject">Consumer-defined identity key (phone, email, user id).</param>
    /// <param name="scope">Consumer-defined scope the code is valid for.</param>
    /// <param name="spec">The code's characters, length and lifetime; a null lifetime takes the configured one.</param>
    /// <param name="origin">Where the request came from, such as the client address; null limits by subject only.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<OtpCreationResult> CreateAsync(string subject, string scope, OtpCodeSpec spec, string? origin, CancellationToken cancellationToken = default);

    /// <summary>Whether a code for <c>(subject, scope)</c>, or one from <paramref name="origin"/>, was created within the configured window.</summary>
    /// <param name="subject">Consumer-defined identity key.</param>
    /// <param name="scope">Scope key.</param>
    /// <param name="origin">Where the request came from; null checks the subject only.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<bool> IsRateLimitedAsync(string subject, string scope, string? origin, CancellationToken cancellationToken = default);

    /// <summary>Verifies <paramref name="code"/> against the latest pending code for <c>(subject, scope)</c>; consumes it on success, counts an attempt on every try.</summary>
    /// <param name="subject">Consumer-defined identity key used at creation.</param>
    /// <param name="code">The code the user entered.</param>
    /// <param name="scope">Scope used at creation.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<OtpVerificationResult> VerifyAsync(string subject, string code, string scope, CancellationToken cancellationToken = default);
}

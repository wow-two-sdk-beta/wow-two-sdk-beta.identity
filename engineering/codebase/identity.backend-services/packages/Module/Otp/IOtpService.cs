namespace WoW.Two.Sdk.Beta.Be.Identity.Otp;

/// <summary>Defines behavior that orchestrates one-time-password creation and verification keyed by <c>(subject, scope)</c>; the subject is any consumer-defined key and the caller owns delivery (see <see cref="IOtpDeliveryHandler"/>).</summary>
public interface IOtpService
{
    /// <summary>
    /// Generates and stores a new code for <c>(subject, scope)</c>, returning it for delivery; fails with
    /// <see cref="OtpFailureReason.RateLimited"/> when one was created within the configured window, or while the subject's
    /// wrong attempts across its codes exceed <see cref="OtpOptions.MaxSubjectFailures"/>. The new code retires every older
    /// one of the subject and scope.
    /// </summary>
    /// <param name="subject">Consumer-defined identity key (phone, email, username).</param>
    /// <param name="scope">Consumer-defined scope the code is valid for (e.g. <c>"crm"</c>, <c>"sign-in"</c>).</param>
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

    /// <summary>
    /// Generates and stores a new code as <see cref="CreateAsync(string, string, OtpCodeSpec, string?, CancellationToken)"/>
    /// does, bound to <paramref name="binding"/>: the code verifies only through
    /// <see cref="VerifyBoundAsync"/> with the same binding. Rate limits, the failure budget and retiring older codes stay
    /// keyed by the subject alone, so several targets sharing one subject (a trusted contact receiving codes for several
    /// accounts) share its limits.
    /// </summary>
    /// <param name="subject">Consumer-defined identity key the limits count by, such as a trusted contact.</param>
    /// <param name="scope">Consumer-defined scope the code is valid for.</param>
    /// <param name="spec">The code's characters, length and lifetime; a null lifetime takes the configured one.</param>
    /// <param name="origin">Where the request came from; null limits by subject only.</param>
    /// <param name="binding">What the code is for, such as one account or one sign-up; it enters the code's digest.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<OtpCreationResult> CreateBoundAsync(string subject, string scope, OtpCodeSpec spec, string? origin, string binding, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies <paramref name="code"/> as <see cref="VerifyAsync"/> does against the latest pending code of the subject,
    /// made by <see cref="CreateBoundAsync"/> for <paramref name="binding"/>; a code bound to another target fails as a
    /// wrong code does and counts as one.
    /// </summary>
    /// <param name="subject">Consumer-defined identity key used at creation.</param>
    /// <param name="code">The code the user entered.</param>
    /// <param name="scope">Scope used at creation.</param>
    /// <param name="binding">The target the code must be bound to.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<OtpVerificationResult> VerifyBoundAsync(string subject, string code, string scope, string binding, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a code for <c>(subject, scope)</c> was created within <see cref="OtpOptions.RateLimitWindow"/>, one from
    /// <paramref name="origin"/> within <see cref="OtpOptions.OriginRateLimitWindow"/>, or the subject is past its failure budget.
    /// </summary>
    /// <param name="subject">Consumer-defined identity key.</param>
    /// <param name="scope">Scope key.</param>
    /// <param name="origin">Where the request came from; null checks the subject only.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<bool> IsRateLimitedAsync(string subject, string scope, string? origin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies <paramref name="code"/> against the latest pending code for <c>(subject, scope)</c>; consumes it on success,
    /// counts an attempt on every try. Older codes were retired when it was made, so none of them verifies again, and a
    /// subject past <see cref="OtpOptions.MaxSubjectFailures"/> fails with <see cref="OtpFailureReason.MaxAttemptsReached"/>
    /// before any comparison.
    /// </summary>
    /// <param name="subject">Consumer-defined identity key used at creation.</param>
    /// <param name="code">The code the user entered.</param>
    /// <param name="scope">Scope used at creation.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<OtpVerificationResult> VerifyAsync(string subject, string code, string scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a code that could not be delivered (<see cref="OtpCreationResult.CodeId"/>), so no code the person never
    /// received waits to be guessed.
    /// </summary>
    /// <param name="codeId">The id the creation returned.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task DiscardAsync(Guid codeId, CancellationToken cancellationToken = default);
}

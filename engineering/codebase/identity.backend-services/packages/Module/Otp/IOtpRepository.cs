namespace WoW.Two.Sdk.Beta.Be.Identity.Otp;

/// <summary>
/// Defines persistence seam for OTP records; defaults to the in-memory store, multi-instance deployments register the
/// database one (<c>AddDatabaseOtpStore</c>). Keys arrive as digests from <see cref="OtpService"/>. "Pending" means not
/// yet consumed — expiry is judged by <see cref="IOtpService"/>, not the store. Counting an attempt and consuming are
/// conditional, so instances verifying the same code at once never pass the attempt limit or spend it twice.
/// </summary>
public interface IOtpRepository
{
    /// <summary>
    /// Whether a pending code for <c>(subjectHash, scope)</c>, or any code from <paramref name="originHash"/> for
    /// <paramref name="scope"/>, was created within <paramref name="window"/> (rate limiting).
    /// </summary>
    /// <param name="subjectHash">The subject's digest.</param>
    /// <param name="scope">Scope key.</param>
    /// <param name="originHash">The origin's digest; null checks the subject only.</param>
    /// <param name="window">Look-back window.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<bool> HasRecentAsync(string subjectHash, string scope, string? originHash, TimeSpan window, CancellationToken cancellationToken = default);

    /// <summary>Persists a new record.</summary>
    /// <param name="record">The record to save.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SaveAsync(OtpRecord record, CancellationToken cancellationToken = default);

    /// <summary>Latest pending (unconsumed) record for <c>(subjectHash, scope)</c>, expired or not; <c>null</c> when none.</summary>
    /// <param name="subjectHash">The subject's digest.</param>
    /// <param name="scope">Scope key.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<OtpRecord?> FindLatestPendingAsync(string subjectHash, string scope, CancellationToken cancellationToken = default);

    /// <summary>Counts one verification attempt against a pending record, only while it holds fewer than <paramref name="maxAttempts"/>.</summary>
    /// <param name="recordId">The record id.</param>
    /// <param name="maxAttempts">The attempts a code allows.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Whether the attempt was counted; false once the limit is reached or the record was spent.</returns>
    Task<bool> TryCountAttemptAsync(Guid recordId, int maxAttempts, CancellationToken cancellationToken = default);

    /// <summary>Marks a pending record consumed so it can never verify again.</summary>
    /// <param name="recordId">The record id.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Whether this call spent it; false when another verification already did.</returns>
    Task<bool> TryConsumeAsync(Guid recordId, CancellationToken cancellationToken = default);
}

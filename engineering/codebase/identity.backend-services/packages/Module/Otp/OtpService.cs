namespace WoW.Two.Sdk.Beta.Be.Identity.Otp;

/// <summary>
/// Provides one-time-code creation and verification. The store receives digests only (<see cref="OtpCodeHasher"/>);
/// every verification first claims an attempt, so the attempt limit holds across instances, and a success spends the
/// code conditionally, so it verifies once.
/// </summary>
public sealed class OtpService : IOtpService
{
    private readonly IOtpRepository _store;
    private readonly IOtpCodeGenerator _codeGenerator;
    private readonly OtpCodeHasher _hasher;
    private readonly OtpOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the service over a store, generator, hasher, options, and time source.</summary>
    /// <param name="store">Record persistence.</param>
    /// <param name="codeGenerator">Code generation strategy.</param>
    /// <param name="hasher">Keys every stored value.</param>
    /// <param name="options">Lifetime / rate-limit / attempt settings.</param>
    /// <param name="timeProvider">Time source for creation, expiry, and rate-limit checks.</param>
    public OtpService(
        IOtpRepository store,
        IOtpCodeGenerator codeGenerator,
        OtpCodeHasher hasher,
        OtpOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(codeGenerator);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _codeGenerator = codeGenerator;
        _hasher = hasher;
        _options = options;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<OtpCreationResult> CreateAsync(string subject, string scope, CancellationToken cancellationToken = default)
        => CreateAsync(subject, scope, _options.DefaultSpec, null, cancellationToken);

    /// <inheritdoc />
    public Task<OtpCreationResult> CreateAsync(string subject, string scope, OtpCodeSpec spec, CancellationToken cancellationToken = default)
        => CreateAsync(subject, scope, spec, null, cancellationToken);

    /// <inheritdoc />
    public async Task<OtpCreationResult> CreateAsync(string subject, string scope, OtpCodeSpec spec, string? origin, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(spec);

        var subjectHash = _hasher.Subject(subject);
        var originHash = string.IsNullOrWhiteSpace(origin) ? null : _hasher.Origin(origin);
        var rateLimited = await _store
            .HasRecentAsync(subjectHash, scope, originHash, _options.RateLimitWindow, cancellationToken)
            .ConfigureAwait(false);
        if (rateLimited)
        {
            return OtpCreationResult.Failed(OtpFailureReason.RateLimited);
        }

        var code = _codeGenerator.Generate(spec);
        var now = _timeProvider.GetUtcNow();
        var record = new OtpRecord
        {
            Id = Guid.CreateVersion7(now),
            SubjectHash = subjectHash,
            CodeHash = _hasher.Code(subjectHash, scope, code),
            OriginHash = originHash,
            Scope = scope,
            CreatedAt = now,
            ExpiresAt = now + (spec.Lifetime ?? _options.CodeLifetime),
            Attempts = 0,
            IsConsumed = false,
        };

        await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        return OtpCreationResult.Succeeded(code, record.ExpiresAt);
    }

    /// <inheritdoc />
    public Task<bool> IsRateLimitedAsync(string subject, string scope, string? origin, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        var originHash = string.IsNullOrWhiteSpace(origin) ? null : _hasher.Origin(origin);
        return _store.HasRecentAsync(_hasher.Subject(subject), scope, originHash, _options.RateLimitWindow, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OtpVerificationResult> VerifyAsync(string subject, string code, string scope, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (string.IsNullOrWhiteSpace(code))
        {
            return OtpVerificationResult.Failed(OtpFailureReason.InvalidCode);
        }

        var subjectHash = _hasher.Subject(subject);
        var record = await _store.FindLatestPendingAsync(subjectHash, scope, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return OtpVerificationResult.Failed(OtpFailureReason.InvalidCode);
        }

        if (_timeProvider.GetUtcNow() > record.ExpiresAt)
        {
            return OtpVerificationResult.Failed(OtpFailureReason.Expired);
        }

        // The attempt is claimed before the comparison, so concurrent tries on several instances share one limit.
        if (!await _store.TryCountAttemptAsync(record.Id, _options.MaxAttempts, cancellationToken).ConfigureAwait(false))
        {
            return OtpVerificationResult.Failed(OtpFailureReason.MaxAttemptsReached);
        }

        if (!OtpCodeHasher.Matches(record.CodeHash, _hasher.Code(subjectHash, scope, code)))
        {
            return OtpVerificationResult.Failed(OtpFailureReason.InvalidCode);
        }

        return await _store.TryConsumeAsync(record.Id, cancellationToken).ConfigureAwait(false)
            ? OtpVerificationResult.Succeeded()
            : OtpVerificationResult.Failed(OtpFailureReason.InvalidCode);
    }
}

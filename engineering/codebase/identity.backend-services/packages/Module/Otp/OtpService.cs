namespace WoW.Two.Sdk.Beta.Be.Identity.Otp;

/// <summary>
/// Provides one-time-code creation and verification. The store receives digests only (<see cref="OtpCodeHasher"/>);
/// every verification first claims an attempt, so the attempt limit holds across instances, and a success spends the
/// code conditionally, so it verifies once. A new code retires the subject's older ones, and its wrong attempts across codes
/// share one budget (<see cref="OtpOptions.MaxSubjectFailures"/> per <see cref="OtpOptions.SubjectFailureWindow"/>), so
/// asking for a new code never buys fresh guesses.
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
    public Task<OtpCreationResult> CreateAsync(string subject, string scope, OtpCodeSpec spec, string? origin, CancellationToken cancellationToken = default)
        => CreateCoreAsync(subject, scope, spec, origin, binding: null, cancellationToken);

    /// <inheritdoc />
    public Task<OtpCreationResult> CreateBoundAsync(string subject, string scope, OtpCodeSpec spec, string? origin, string binding, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binding);
        return CreateCoreAsync(subject, scope, spec, origin, binding, cancellationToken);
    }

    private async Task<OtpCreationResult> CreateCoreAsync(string subject, string scope, OtpCodeSpec spec, string? origin, string? binding, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(spec);

        var subjectHash = _hasher.Subject(subject);
        var originHash = string.IsNullOrWhiteSpace(origin) ? null : _hasher.Origin(origin);
        if (await IsLimitedAsync(subjectHash, scope, originHash, cancellationToken).ConfigureAwait(false))
        {
            return OtpCreationResult.Failed(OtpFailureReason.RateLimited);
        }

        var code = _codeGenerator.Generate(spec);
        var now = _timeProvider.GetUtcNow();

        // The new code replaces every older one: none of them verifies again, even once the new one is spent.
        await _store.RetirePendingAsync(subjectHash, scope, now, cancellationToken).ConfigureAwait(false);
        var record = new OtpRecord
        {
            Id = Guid.CreateVersion7(now),
            SubjectHash = subjectHash,
            CodeHash = _hasher.Code(subjectHash, DigestScope(scope, binding), code),
            OriginHash = originHash,
            Scope = scope,
            CreatedAt = now,
            ExpiresAt = now + (spec.Lifetime ?? _options.CodeLifetime),
            Attempts = 0,
            IsConsumed = false,
        };

        await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        return OtpCreationResult.Succeeded(code, record.ExpiresAt, record.Id);
    }

    /// <inheritdoc />
    public Task<bool> IsRateLimitedAsync(string subject, string scope, string? origin, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        var originHash = string.IsNullOrWhiteSpace(origin) ? null : _hasher.Origin(origin);
        return IsLimitedAsync(_hasher.Subject(subject), scope, originHash, cancellationToken);
    }

    /// <inheritdoc />
    public Task DiscardAsync(Guid codeId, CancellationToken cancellationToken = default)
        => _store.DeleteAsync(codeId, cancellationToken);

    /// <inheritdoc />
    public Task<OtpVerificationResult> VerifyAsync(string subject, string code, string scope, CancellationToken cancellationToken = default)
        => VerifyCoreAsync(subject, code, scope, binding: null, cancellationToken);

    /// <inheritdoc />
    public Task<OtpVerificationResult> VerifyBoundAsync(string subject, string code, string scope, string binding, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binding);
        return VerifyCoreAsync(subject, code, scope, binding, cancellationToken);
    }

    private async Task<OtpVerificationResult> VerifyCoreAsync(string subject, string code, string scope, string? binding, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (string.IsNullOrWhiteSpace(code))
        {
            return OtpVerificationResult.Failed(OtpFailureReason.InvalidCode);
        }

        var subjectHash = _hasher.Subject(subject);
        if (await IsPastFailureBudgetAsync(subjectHash, scope, cancellationToken).ConfigureAwait(false))
        {
            return OtpVerificationResult.Failed(OtpFailureReason.MaxAttemptsReached);
        }

        // Every older code was retired when the latest was made, so once that is spent no earlier one comes back.
        var record = await _store.FindLatestPendingAsync(subjectHash, scope, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return OtpVerificationResult.Failed(OtpFailureReason.InvalidCode);
        }

        if (_timeProvider.GetUtcNow() >= record.ExpiresAt)
        {
            return OtpVerificationResult.Failed(OtpFailureReason.Expired);
        }

        // The attempt is claimed before the comparison, so concurrent tries on several instances share one limit.
        if (!await _store.TryCountAttemptAsync(record.Id, _options.MaxAttempts, cancellationToken).ConfigureAwait(false))
        {
            return OtpVerificationResult.Failed(OtpFailureReason.MaxAttemptsReached);
        }

        if (!OtpCodeHasher.Matches(record.CodeHash, _hasher.Code(subjectHash, DigestScope(scope, binding), code)))
        {
            return OtpVerificationResult.Failed(OtpFailureReason.InvalidCode);
        }

        return await _store.TryConsumeAsync(record.Id, cancellationToken).ConfigureAwait(false)
            ? OtpVerificationResult.Succeeded()
            : OtpVerificationResult.Failed(OtpFailureReason.InvalidCode);
    }

    /// <summary>
    /// Whether a new code must wait: one for the subject went out within <see cref="OtpOptions.RateLimitWindow"/>, one from
    /// the origin within <see cref="OtpOptions.OriginRateLimitWindow"/>, or the subject spent its failure budget.
    /// </summary>
    /// <summary>The scope a code's digest covers: the scope alone, or the scope with the code's binding, so a bound code verifies only for its target.</summary>
    private static string DigestScope(string scope, string? binding) => binding is null ? scope : string.Concat(scope, "\nbound:", binding);

    private async Task<bool> IsLimitedAsync(string subjectHash, string scope, string? originHash, CancellationToken cancellationToken)
        => await _store.HasRecentAsync(subjectHash, scope, null, _options.RateLimitWindow, cancellationToken).ConfigureAwait(false)
            || (originHash is not null
                && await _store.HasRecentAsync(subjectHash, scope, originHash, _options.OriginRateLimitWindow, cancellationToken).ConfigureAwait(false))
            || await IsPastFailureBudgetAsync(subjectHash, scope, cancellationToken).ConfigureAwait(false);

    /// <summary>Whether the subject's wrong attempts across its codes of <paramref name="scope"/> reached <see cref="OtpOptions.MaxSubjectFailures"/> within the window.</summary>
    private async Task<bool> IsPastFailureBudgetAsync(string subjectHash, string scope, CancellationToken cancellationToken)
    {
        var since = _timeProvider.GetUtcNow() - _options.SubjectFailureWindow;
        var failures = await _store.CountFailedAttemptsAsync(subjectHash, scope, since, cancellationToken).ConfigureAwait(false);
        return failures >= _options.MaxSubjectFailures;
    }
}

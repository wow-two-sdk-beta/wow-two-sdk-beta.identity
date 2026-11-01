namespace WoW.Two.Sdk.Beta.Be.Identity.Otp;

/// <summary>
/// One stored OTP, the canonical shape every <see cref="IOtpRepository"/> maps onto. It holds digests only:
/// <see cref="OtpService"/> keys the subject, the code and the origin with <see cref="OtpOptions.HashKey"/>, so a store
/// never sees a code, a phone number or an address.
/// </summary>
public sealed record OtpRecord
{
    /// <summary>Stable record id.</summary>
    public required Guid Id { get; init; }

    /// <summary>The digest of the consumer-defined identity key (phone, email, user id).</summary>
    public required string SubjectHash { get; init; }

    /// <summary>The digest of the code, bound to the subject and the scope.</summary>
    public required string CodeHash { get; init; }

    /// <summary>The digest of where the request came from, such as the client address; null when the caller named none.</summary>
    public string? OriginHash { get; init; }

    /// <summary>Consumer-defined scope the code is valid for.</summary>
    public required string Scope { get; init; }

    /// <summary>Creation instant (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Expiry instant (UTC).</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Verification attempts counted so far, the successful one included.</summary>
    public required int Attempts { get; init; }

    /// <summary>Whether the code was successfully used.</summary>
    public required bool IsConsumed { get; init; }
}

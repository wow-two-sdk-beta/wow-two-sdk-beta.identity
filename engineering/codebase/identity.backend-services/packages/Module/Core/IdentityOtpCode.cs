namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>
/// One one-time code as the database OTP store keeps it (<c>identity_otp_codes</c>), shared by every instance on the
/// schema. Digests only: the subject, the code and the origin are keyed with <c>Identity:Otp:HashKey</c>, so a copy of the
/// table reveals no code, phone number or address. Rows past their expiry are pruned as new ones arrive.
/// </summary>
public sealed class IdentityOtpCode
{
    /// <summary>Primary key, a Guid v7 made in code.</summary>
    public Guid Id { get; set; }

    /// <summary>The digest of the identity key the code was made for (a phone number, a user id).</summary>
    public string SubjectHash { get; set; } = string.Empty;

    /// <summary>What the code is for, such as <c>identity.phone-sign-in</c>.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>The digest of the code, bound to the subject and the scope.</summary>
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>The digest of where the request came from, such as the client address; null when the caller named none.</summary>
    public string? OriginHash { get; set; }

    /// <summary>Verification attempts counted so far, the successful one included.</summary>
    public int Attempts { get; set; }

    /// <summary>When the code was made (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the code stops verifying (UTC).</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>When the code was spent; null while pending.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }
}

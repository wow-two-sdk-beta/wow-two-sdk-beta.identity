namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>
/// One sign-in attempt, successful or not: when, who (when known), how, from where and how it ended. Rows are written
/// once and deleted after the retention window. Consumed by the sign-in log slice.
/// </summary>
/// <remarks>Personal data stays masked: the typed login (<see cref="SubjectMasked"/>) and the address (<see cref="IpMasked"/>) never land in full.</remarks>
public sealed class IdentitySignInLogEntry
{
    /// <summary>Primary key, a database identity.</summary>
    public long Id { get; set; }

    /// <summary>When the attempt happened (UTC).</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>The account the attempt named, when one exists; null for an unknown login.</summary>
    public Guid? UserId { get; set; }

    /// <summary>The login the person typed, masked: an email keeps its first letter and domain, a phone its prefix and last two digits.</summary>
    public string? SubjectMasked { get; set; }

    /// <summary>The step attempted, one of <c>SignInMethodConstants</c> or a second-factor method name.</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>The OIDC client the sign-in serves, when the caller named one.</summary>
    public string? ClientId { get; set; }

    /// <summary>How it ended, one of <c>SignInResultConstants</c>.</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>Why it did not succeed, one of <c>SignInReasonConstants</c>; null on success.</summary>
    public string? Reason { get; set; }

    /// <summary>The address it came from, masked to its network (IPv4 /24, IPv6 /48).</summary>
    public string? IpMasked { get; set; }

    /// <summary>The browser's user agent, cut to 256 characters.</summary>
    public string? UserAgent { get; set; }

    /// <summary>The session a successful cookie sign-in started.</summary>
    public Guid? SessionId { get; set; }

    /// <summary>The W3C trace id of the request, to join the attempt with its logs and spans.</summary>
    public string? TraceId { get; set; }
}

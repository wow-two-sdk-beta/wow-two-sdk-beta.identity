namespace WoW.Two.Sdk.Beta.Be.Identity.Otp;

/// <summary>Holds the inputs that steer one-time code creation, verification and message wording.</summary>
/// <remarks>Set in code with <c>AddOtpService(o => …)</c> or in the host section <c>Identity:Otp</c>, which is applied last.</remarks>
public sealed record OtpOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Identity:Otp";

    /// <summary>Gets or sets the characters in a generated code (4–12). Default 6.</summary>
    public int CodeLength { get; set; } = 6;

    /// <summary>Gets or sets the characters a code draws from. Default <see cref="OtpCodeKind.Numeric"/>.</summary>
    public OtpCodeKind CodeKind { get; set; } = OtpCodeKind.Numeric;

    /// <summary>Gets or sets how long a code stays valid. Default 5 minutes.</summary>
    public TimeSpan CodeLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the minimum gap between two codes for the same <c>(subject, scope)</c>: the resend window, which also
    /// spaces the Telegram start links of one number. Default 30s.
    /// </summary>
    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the minimum gap between two codes (or Telegram start links) of one scope asked from the same origin, such
    /// as a client address. Kept short, since many people can share one address behind a carrier's NAT. Default 5s.
    /// </summary>
    public TimeSpan OriginRateLimitWindow { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets the wrong attempts allowed against one code before it locks. Default 5.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// Gets or sets the wrong attempts one subject may make across all its codes of a scope within
    /// <see cref="SubjectFailureWindow"/>; past it, no code of that scope is issued to the subject or verified for it until
    /// the window moves past the failures. A new code brings no fresh budget, so codes cannot be guessed one after another.
    /// Default 10.
    /// </summary>
    public int MaxSubjectFailures { get; set; } = 10;

    /// <summary>Gets or sets the window <see cref="MaxSubjectFailures"/> counts within. Default 1 hour.</summary>
    public TimeSpan SubjectFailureWindow { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the base64 or base64url key (padding optional, 32 bytes or more) every stored code, subject and origin is keyed with; a secret.
    /// Every instance sharing a database store needs the same key. Unset, each process draws its own: enough for one
    /// process, and its live codes stop verifying on restart.
    /// </summary>
    public string? HashKey { get; set; }

    /// <summary>Gets the wording of delivered codes, per culture.</summary>
    public OtpMessageOptions Messages { get; } = new();

    /// <summary>The spec a code takes when its caller names none.</summary>
    internal OtpCodeSpec DefaultSpec => new() { Kind = CodeKind, Length = CodeLength, Lifetime = CodeLifetime };
}

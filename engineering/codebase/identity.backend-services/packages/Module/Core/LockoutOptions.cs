namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>Holds lockout rules (consumed by the lockout slice).</summary>
/// <remarks>Set in code with <c>AddUserAccounts(o =&gt; o.Lockout…)</c> or in the host section <c>Identity:Lockout</c>, which is applied last.</remarks>
public sealed record LockoutOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Identity:Lockout";

    /// <summary>Failed attempts before lockout. Default 5.</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>How long an account stays locked out. Default 5 minutes.</summary>
    public TimeSpan DefaultLockout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Whether a newly created account starts with lockout enabled. Default true.</summary>
    public bool IsEnabledForNewUsers { get; set; } = true;
}

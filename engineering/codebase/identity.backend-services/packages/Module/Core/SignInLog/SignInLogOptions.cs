namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;

/// <summary>Holds how long the sign-in log keeps its rows and how often its cleanup runs.</summary>
/// <remarks>Set in code with <c>.AddSignInLog(o => …)</c> or in the host section <c>Identity:SignInLog</c>, which is applied last.</remarks>
public sealed record SignInLogOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Identity:SignInLog";

    /// <summary>Gets or sets how many days a row is kept; older rows are deleted. Default 180.</summary>
    public int RetentionDays { get; set; } = 180;

    /// <summary>Gets or sets how often the cleanup job deletes rows past the retention; it also runs once at startup. Default 1 hour.</summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
}

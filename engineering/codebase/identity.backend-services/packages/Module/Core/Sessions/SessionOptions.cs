namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;

/// <summary>Holds how long a sign-in session lasts and how often its last use is recorded.</summary>
/// <remarks>Set in code with <c>.AddSessions(o => …)</c> or in the host section <c>Identity:Sessions</c>, which is applied last.</remarks>
public sealed record SessionOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Identity:Sessions";

    /// <summary>Gets or sets the absolute lifetime of a session from sign-in; the cookie expires with it. Default 12 hours.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Gets or sets how stale <c>last_seen_at</c> may grow before a request refreshes it. Default 5 minutes.</summary>
    public TimeSpan ActivityInterval { get; set; } = TimeSpan.FromMinutes(5);
}

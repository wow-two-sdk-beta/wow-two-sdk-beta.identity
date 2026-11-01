namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TestMode;

/// <summary>Holds the switch of test mode: a local run whose outgoing messages stay in memory and whose Telegram needs no real bot.</summary>
/// <remarks>
/// Set in code with <c>.AddTestMode(o => …)</c> or in the host section <c>Identity:TestMode</c>, which is applied last. It is
/// read at start, and a deployed boot (anything but Development without <c>DEPLOY_ENVIRONMENT</c>) refuses it: test mode
/// hands every code the instance sends to any caller.
/// </remarks>
public sealed record TestModeOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Identity:TestMode";

    /// <summary>Gets or sets whether test mode is on. Default <c>false</c>.</summary>
    public bool IsEnabled { get; set; }
}

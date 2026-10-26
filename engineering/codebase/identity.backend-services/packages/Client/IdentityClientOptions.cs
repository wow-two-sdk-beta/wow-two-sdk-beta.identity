namespace WoW.Two.Sdk.Beta.Be.Identity.Client;

/// <summary>Holds the settings of the Identity HTTP client.</summary>
public sealed class IdentityClientOptions
{
    /// <summary>Gets or sets the absolute root URL of the running Identity service.</summary>
    public Uri? BaseAddress { get; set; }
}

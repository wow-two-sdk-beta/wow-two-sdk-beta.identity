namespace WoW.Two.Sdk.Beta.Be.Identity.Module;

/// <summary>Holds the settings of the in-process Identity module.</summary>
public sealed class IdentityModuleOptions
{
    /// <summary>Gets or sets the SQLite connection string of the store that holds the Identity tables.</summary>
    public string ConnectionString { get; set; } = string.Empty;
}

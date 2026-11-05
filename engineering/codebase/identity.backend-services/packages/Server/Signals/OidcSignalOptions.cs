namespace WoW.Two.Sdk.Beta.Be.Identity.Signals;

/// <summary>Holds how revocation signals reach the services that named a signal URL (<c>Identity:Server:Signals</c>).</summary>
public sealed record OidcSignalOptions
{
    /// <summary>Gets or sets whether a signal goes only over HTTPS. Default true.</summary>
    public bool RequireHttps { get; set; } = true;

    /// <summary>Gets or sets whether a signal may reach a service on a private network, such as the instance's own. Default false.</summary>
    public bool AllowPrivateNetworkTargets { get; set; }

    /// <summary>Gets or sets how long one delivery attempt may take, so a service that hangs holds nobody up for long. Default 5 seconds.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(5);
}

namespace WoW.Two.Sdk.Beta.Be.Identity.Signals;

/// <summary>Holds how long the token deny list keeps an entry whose signal didn't say.</summary>
public sealed record DenyListOptions
{
    /// <summary>Gets or sets an entry's lifetime when a signal names no <c>TokensExpireBy</c>. Default 1 hour.</summary>
    public TimeSpan DefaultEntryLifetime { get; set; } = TimeSpan.FromHours(1);
}

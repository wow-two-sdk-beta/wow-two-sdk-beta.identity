namespace WoW.Two.Sdk.Beta.Be.Identity.Signals;

/// <summary>Holds the names the signal receiver registers under.</summary>
public static class IdentitySignalReceiverConstants
{
    /// <summary>The inbound webhook receiver's name; repeated delivery ids are dropped under it.</summary>
    public const string ReceiverName = "identity-signals";

    /// <summary>Holds the request item marking a signal whose signature couldn't be checked yet, such as with the issuer's discovery down.</summary>
    internal const string UncheckableItem = "WoW.Two.Sdk.Beta.Be.Identity.Signals.Uncheckable";
}

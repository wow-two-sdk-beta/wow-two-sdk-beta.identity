namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>A trusted contact a sign-in by an account's own number may send its code to instead.</summary>
public sealed record TrustedContactTarget
{
    /// <summary>The opaque id <c>phone-sign-in/send</c> takes as <c>targetId</c>: a keyed digest of the account and the contact.</summary>
    public required string Id { get; init; }

    /// <summary>The contact's masked name and phone, such as <c>S*** R*** · +998*******45</c>.</summary>
    public required string Label { get; init; }
}

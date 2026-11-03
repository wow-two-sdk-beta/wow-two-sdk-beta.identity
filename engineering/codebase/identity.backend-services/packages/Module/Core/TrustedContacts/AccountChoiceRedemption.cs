namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>A redeemed account choice: the account to sign in as, and the contact the sign-in goes through.</summary>
public sealed record AccountChoiceRedemption
{
    /// <summary>The account picked.</summary>
    public required IdentityUser User { get; init; }

    /// <summary>The contact whose code proved the choice, when the account is not their own; null for their own.</summary>
    public Guid? TrustedContactUserId { get; init; }
}

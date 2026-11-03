using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>
/// One trusted contact of an account (<c>identity_trusted_contacts</c>, <c>010-trusted-contacts</c>): another person of the
/// instance whose confirmed, bot-linked phone receives the account's sign-in codes, with the same power as the account's
/// own phone. One row per pair (composite key user + contact); at most <c>Identity:TrustedContacts:MaxPerPerson</c> per user.
/// </summary>
public sealed class IdentityTrustedContact : ICompositeKeyEntity
{
    /// <summary>The account the contact signs in for.</summary>
    public Guid UserId { get; set; }

    /// <summary>The trusted contact: the person whose Telegram receives the account's codes.</summary>
    public Guid ContactUserId { get; set; }

    /// <summary>Who named the contact, such as the inviter who made the invitation.</summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>When the contact was recorded (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

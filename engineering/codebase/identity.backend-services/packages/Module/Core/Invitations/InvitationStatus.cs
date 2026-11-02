namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;

/// <summary>Where an invitation stands.</summary>
public enum InvitationStatus
{
    /// <summary>It redeems: unused, standing and inside its lifetime.</summary>
    Pending,

    /// <summary>It is past its end, unused.</summary>
    Expired,

    /// <summary>A person redeemed it.</summary>
    Accepted,

    /// <summary>An owner or admin withdrew it.</summary>
    Revoked,
}

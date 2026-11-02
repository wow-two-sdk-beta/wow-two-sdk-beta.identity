namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;

/// <summary>Represents an invitation found by its link's token, with its organization and where it stands.</summary>
public sealed record InvitationLookup
{
    /// <summary>Gets the invitation.</summary>
    public required IdentityInvitation Invitation { get; init; }

    /// <summary>Gets the organization the person joins.</summary>
    public required IdentityOrganization Organization { get; init; }

    /// <summary>Gets where the invitation stands now.</summary>
    public required InvitationStatus Status { get; init; }
}

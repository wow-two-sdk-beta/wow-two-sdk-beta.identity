namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;

/// <summary>Represents the outcome of making an invitation: the stored row with its token and link, or why it was refused.</summary>
public sealed record InvitationCreateResult
{
    /// <summary>Gets success, or the refusal with its code.</summary>
    public required IdentityResult Result { get; init; }

    /// <summary>Gets the stored invitation; null when refused.</summary>
    public IdentityInvitation? Invitation { get; init; }

    /// <summary>Gets the link's token, shown this once; null when refused.</summary>
    public string? Token { get; init; }

    /// <summary>Gets the link that redeems it (<see cref="InvitationOptions.LinkTemplate"/>); null without a template or when refused.</summary>
    public string? Link { get; init; }

    /// <summary>Gets whether the invitation was made.</summary>
    public bool IsSuccess => Result.IsSuccess;
}

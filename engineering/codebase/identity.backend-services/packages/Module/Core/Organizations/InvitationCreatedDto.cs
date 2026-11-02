namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

/// <summary>A new invitation with its link, which this answer alone carries.</summary>
public sealed record InvitationCreatedDto
{
    /// <summary>The invitation.</summary>
    public required InvitationDto Invitation { get; init; }

    /// <summary>The link's token, shown once.</summary>
    public required string Token { get; init; }

    /// <summary>The link to send, on the hosted sign-up page; null when the instance names no origin.</summary>
    public string? Link { get; init; }
}

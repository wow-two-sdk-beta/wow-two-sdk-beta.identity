namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;

/// <summary>Represents how making an invitation from the host CLI ended.</summary>
public sealed record AdminInvitationResult
{
    /// <summary>Gets the outcome.</summary>
    public required AdminInvitationStatus Status { get; init; }

    /// <summary>Gets the organization's id once found.</summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>Gets the invitation's id once made.</summary>
    public Guid? InvitationId { get; init; }

    /// <summary>Gets the role in the catalog's spelling once made.</summary>
    public string? Role { get; init; }

    /// <summary>Gets the link that redeems it (<c>Identity:Invitations:LinkTemplate</c>); null without a template.</summary>
    public string? Link { get; init; }

    /// <summary>Gets the link's token, shown this once; null when refused.</summary>
    public string? Token { get; init; }

    /// <summary>Gets when it stops working (UTC), once made.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Gets why a run was refused or invalid; null otherwise.</summary>
    public string? Error { get; init; }

    internal static AdminInvitationResult Refused(string error) => new() { Status = AdminInvitationStatus.Refused, Error = error };

    internal static AdminInvitationResult Invalid(string error, Guid? organizationId = null)
        => new() { Status = AdminInvitationStatus.Invalid, Error = error, OrganizationId = organizationId };
}

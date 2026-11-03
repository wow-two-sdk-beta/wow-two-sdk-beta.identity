namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

/// <summary>One trusted contact of a member, as the organization's owners and admins see it.</summary>
public sealed record MemberTrustedContactDto
{
    /// <summary>The contact.</summary>
    public required Guid UserId { get; init; }

    /// <summary>The name the contact goes by, or their masked phone when they set none.</summary>
    public string? DisplayName { get; init; }
}

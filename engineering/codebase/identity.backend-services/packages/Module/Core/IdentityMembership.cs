using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>Join row placing a user in an organization with one role (composite key organization + user).</summary>
public sealed class IdentityMembership : ICompositeKeyEntity
{
    /// <summary>The organization.</summary>
    public Guid OrganizationId { get; set; }

    /// <summary>The member.</summary>
    public Guid UserId { get; set; }

    /// <summary>The member's role in the organization, one of the configured role names.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>When the user joined (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

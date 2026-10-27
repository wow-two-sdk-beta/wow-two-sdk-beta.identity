using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>Join row assigning a role to a user (composite key user + role). Consumed by the roles slice.</summary>
public sealed class IdentityUserRole : ICompositeKeyEntity
{
    /// <summary>The user.</summary>
    public Guid UserId { get; set; }

    /// <summary>The role.</summary>
    public Guid RoleId { get; set; }
}

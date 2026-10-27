using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>A claim carried by a role, granted to every user in it.</summary>
public sealed class IdentityRoleClaim : IKeyedEntity<long>
{
    /// <summary>Surrogate primary key: a store-generated <c>bigint</c> identity, in insertion order.</summary>
    public long Id { get; set; }

    /// <summary>The owning role.</summary>
    public Guid RoleId { get; set; }

    /// <summary>Claim type (URI or short name).</summary>
    public string? ClaimType { get; set; }

    /// <summary>Claim value.</summary>
    public string? ClaimValue { get; set; }
}

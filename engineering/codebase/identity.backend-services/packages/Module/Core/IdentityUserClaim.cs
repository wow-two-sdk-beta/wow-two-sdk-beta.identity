using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>A claim held by a user.</summary>
public sealed class IdentityUserClaim : IKeyedEntity<long>
{
    /// <summary>Surrogate primary key: a store-generated <c>bigint</c> identity, in insertion order.</summary>
    public long Id { get; set; }

    /// <summary>The owning user.</summary>
    public Guid UserId { get; set; }

    /// <summary>Claim type (URI or short name).</summary>
    public string? ClaimType { get; set; }

    /// <summary>Claim value.</summary>
    public string? ClaimValue { get; set; }
}

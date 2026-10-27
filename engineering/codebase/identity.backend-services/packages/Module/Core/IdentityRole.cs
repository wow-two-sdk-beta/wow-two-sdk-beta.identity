using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>A role, with ASP.NET-Identity-shaped columns. Consumed by the roles slice; the identity schema always holds its table, so turning the slice on needs no migration.</summary>
public sealed class IdentityRole : IKeyedEntity<Guid>, IAuditable
{
    /// <summary>Primary key: a uuid v7 the role service assigns from <see cref="TimeProvider"/> at creation.</summary>
    public Guid Id { get; set; }

    /// <summary>Role name.</summary>
    public string? Name { get; set; }

    /// <summary>Upper-invariant <see cref="Name"/> for case-insensitive unique lookup.</summary>
    public string? NormalizedName { get; set; }

    /// <summary>Value that changes on every persist — optimistic-concurrency guard.</summary>
    public string? ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();

    /// <summary>When the row was created (UTC) — stamped by the audit interceptor.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the row was last modified (UTC) — stamped by the audit interceptor.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

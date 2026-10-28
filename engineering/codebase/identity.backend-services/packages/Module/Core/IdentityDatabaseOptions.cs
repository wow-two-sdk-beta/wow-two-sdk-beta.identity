using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>Holds where the Module's identity schema lives: the connection, the PostgreSQL schema and its owner kind.</summary>
/// <remarks>
///   - set in code with <c>AddUserAccounts(configureDatabase: o => …)</c> or in the host section <c>Identity:Database</c>,
///     which is applied last
///   - PostgreSQL only: the SQLite dialect retired on 2026-10-27, as products may not run SQLite
/// </remarks>
public sealed record IdentityDatabaseOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Identity:Database";

    /// <summary>The be sdk migration set the Module's embedded SQL runs as; its history is <c>identity_migration_history</c>.</summary>
    public const string MigrationSetName = "identity";

    /// <summary>
    /// The tables the <see cref="MigrationSetName"/> set publishes to other owners in its database: the key a product's
    /// set may reference, and the columns it may read.
    /// </summary>
    /// <remarks>
    ///   - readable columns are stable, non-secret display columns: never a hash, stamp, phone or lockout column
    ///   - <c>identity_users</c> rows are never removed: a deleted account keeps its id with <c>deleted_at</c> set, and
    ///     erasure clears its names and email in place, so a <c>RESTRICT</c> key never blocks
    ///   - changing a list is part of the client contract: expand and contract only
    /// </remarks>
    public static IReadOnlyList<MigrationReferenceableTable> ReferenceableTables { get; } =
    [
        new("identity_users", ["id"], ["id", "user_name", "email", "created_at", "updated_at", "deleted_at"]),
        new("identity_organizations", ["id"], ["id", "name", "slug", "created_at"]),
    ];

    /// <summary>Gets or sets the PostgreSQL schema holding the identity tables, one per instance. Default <c>identity</c>.</summary>
    /// <remarks>A lowercase identifier: <c>a-z</c> first, then <c>a-z</c>, <c>0-9</c> and <c>_</c>, 63 characters at most.</remarks>
    public string Schema { get; set; } = "identity";

    /// <summary>Gets or sets the name under <c>ConnectionStrings</c> the identity database is read from; null uses the host's own database.</summary>
    /// <remarks>The host's database is the registered <c>DatabaseSettings</c>, else <c>DB_CONNECTION</c>, else <c>DatabaseSettings:ConnectionString</c>.</remarks>
    public string? ConnectionStringName { get; set; }

    /// <summary>Gets or sets the kind of owner the host claims the schema as: <see cref="IdentitySchemaOwnerConstants.Module"/> (default) or <see cref="IdentitySchemaOwnerConstants.Service"/>.</summary>
    /// <remarks>A schema one kind claimed refuses the other's migrations, so a Form 1 product cannot migrate an instance's schema.</remarks>
    public string SchemaOwner { get; set; } = IdentitySchemaOwnerConstants.Module;
}

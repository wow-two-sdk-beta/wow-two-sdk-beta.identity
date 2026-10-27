using System.Data.Common;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using WoW.Two.Sdk.Beta.Be.Identity.Core;

namespace Identity.Tests.Migrations;

/// <summary>One table as PostgreSQL stores it: its columns, primary key and indexes, in name order.</summary>
public sealed record TableShape(string Name, IReadOnlyList<ColumnShape> Columns, KeyShape PrimaryKey, IReadOnlyList<IndexShape> Indexes);

/// <summary>One column: its store type (lower case), nullability and whether the database generates it.</summary>
public sealed record ColumnShape(string Name, string Type, bool Nullable, bool Generated);

/// <summary>A primary key: its constraint name and columns in key order.</summary>
public sealed record KeyShape(string Name, string Columns);

/// <summary>An index: its name, columns in index order and uniqueness.</summary>
public sealed record IndexShape(string Name, string Columns, bool Unique);

/// <summary>Reads the identity tables' shape from the EF model and from an applied database, so a test compares them.</summary>
public static class SchemaShape
{
    /// <summary>The 11 tables 001-accounts creates.</summary>
    public static readonly IReadOnlyList<string> AccountTables =
    [
        "identity_memberships", "identity_organizations", "identity_passkeys", "identity_refresh_tokens", "identity_role_claims",
        "identity_roles", "identity_user_claims", "identity_user_logins", "identity_user_roles", "identity_user_tokens", "identity_users",
    ];

    /// <summary>OpenIddict's 4 tables 002-oidc creates.</summary>
    public static readonly IReadOnlyList<string> OidcTables =
    [
        "identity_oidc_applications", "identity_oidc_authorizations", "identity_oidc_scopes", "identity_oidc_tokens",
    ];

    /// <summary>The 3 tables 003-spine creates: sessions, the sign-in log and the Data Protection ring.</summary>
    public static readonly IReadOnlyList<string> SpineTables =
    [
        "identity_data_protection_keys", "identity_sessions", "identity_sign_in_log",
    ];

    /// <summary>Every table the set creates, in name order.</summary>
    public static readonly IReadOnlyList<string> AllTables = [.. AccountTables.Concat(OidcTables).Concat(SpineTables).Order(StringComparer.Ordinal)];

    /// <summary>The set's migrations in apply order.</summary>
    public static readonly IReadOnlyList<string> Migrations = ["001-accounts", "002-oidc", "003-spine"];

    /// <summary>The identity set's own history table, which the model does not map.</summary>
    private const string MigrationHistoryTable = $"{IdentityDatabaseOptions.MigrationSetName}_migration_history";

    /// <summary>Builds an <see cref="IdentityDbContext"/> over <paramref name="connectionString"/>, mapping <paramref name="schema"/>.</summary>
    public static IdentityDbContext CreateContext(string connectionString, string schema)
    {
        var builder = new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString);
        return new IdentityDbContext(builder.Options, new IdentityDatabaseOptions { Schema = schema });
    }

    /// <summary>The tables the context's model maps, as its provider would create them.</summary>
    public static IReadOnlyList<TableShape> FromModel(IdentityDbContext context)
    {
        var model = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        return model.Tables
            .Select(table => new TableShape(
                table.Name,
                table.Columns
                    .Select(column => new ColumnShape(
                        column.Name,
                        column.StoreType.ToLowerInvariant(),
                        column.IsNullable,
                        column.PropertyMappings.Any(mapping => mapping.Property.ValueGenerated == ValueGenerated.OnAdd)))
                    .OrderBy(column => column.Name, StringComparer.Ordinal)
                    .ToList(),
                new KeyShape(table.PrimaryKey!.Name, string.Join(',', table.PrimaryKey.Columns.Select(column => column.Name))),
                table.Indexes
                    .Select(index => new IndexShape(index.Name, string.Join(',', index.Columns.Select(column => column.Name)), index.IsUnique))
                    .OrderBy(index => index.Name, StringComparer.Ordinal)
                    .ToList()))
            .OrderBy(table => table.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The identity tables an applied database holds, read from its catalog; the migrator's own tables are left out.</summary>
    public static async Task<IReadOnlyList<TableShape>> FromDatabaseAsync(DbConnection connection, string schema)
    {
        var columns = await connection.QueryAsync<(string Table, string Column, string Type, bool Nullable, bool Generated)>(
            """
            select c.relname, a.attname, format_type(a.atttypid, a.atttypmod), not a.attnotnull, a.attidentity <> ''
            from pg_attribute a
            join pg_class c on c.oid = a.attrelid
            join pg_namespace n on n.oid = c.relnamespace
            where n.nspname = @schema and c.relkind = 'r' and a.attnum > 0 and not a.attisdropped
            """,
            new { schema });
        var keys = await connection.QueryAsync<(string Table, string Name, string Columns)>(
            """
            select c.relname, con.conname,
                   array_to_string(array(
                       select a.attname from unnest(con.conkey) with ordinality k(attnum, ord)
                       join pg_attribute a on a.attrelid = con.conrelid and a.attnum = k.attnum
                       order by k.ord), ',')
            from pg_constraint con
            join pg_class c on c.oid = con.conrelid
            join pg_namespace n on n.oid = c.relnamespace
            where n.nspname = @schema and con.contype = 'p'
            """,
            new { schema });
        var indexes = await connection.QueryAsync<(string Table, string Name, string Columns, bool Unique)>(
            """
            select t.relname, x.relname,
                   array_to_string(array(
                       select a.attname from unnest(i.indkey::int2[]) with ordinality k(attnum, ord)
                       join pg_attribute a on a.attrelid = i.indrelid and a.attnum = k.attnum
                       order by k.ord), ','),
                   i.indisunique
            from pg_index i
            join pg_class t on t.oid = i.indrelid
            join pg_class x on x.oid = i.indexrelid
            join pg_namespace n on n.oid = t.relnamespace
            where n.nspname = @schema and not i.indisprimary
            """,
            new { schema });

        return Assemble(
            columns.Select(c => (c.Table, new ColumnShape(c.Column, c.Type, c.Nullable, c.Generated))),
            keys.ToDictionary(k => k.Table, k => new KeyShape(k.Name, k.Columns)),
            indexes.Select(i => (i.Table, new IndexShape(i.Name, i.Columns, i.Unique))));
    }

    private static List<TableShape> Assemble(
        IEnumerable<(string Table, ColumnShape Column)> columns,
        IReadOnlyDictionary<string, KeyShape> keys,
        IEnumerable<(string Table, IndexShape Index)> indexes)
    {
        var indexesByTable = indexes.ToLookup(i => i.Table, i => i.Index);
        return columns
            .Where(c => c.Table.StartsWith("identity_", StringComparison.Ordinal) && c.Table != MigrationHistoryTable)
            .GroupBy(c => c.Table)
            .Select(group => new TableShape(
                group.Key,
                group.Select(c => c.Column).OrderBy(c => c.Name, StringComparer.Ordinal).ToList(),
                keys.TryGetValue(group.Key, out var key) ? key : new KeyShape("(none)", string.Empty),
                indexesByTable[group.Key].OrderBy(i => i.Name, StringComparer.Ordinal).ToList()))
            .OrderBy(table => table.Name, StringComparer.Ordinal)
            .ToList();
    }
}

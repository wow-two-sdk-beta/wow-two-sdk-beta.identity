using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Sqlite;
using WoW.Two.Sdk.Beta.Be.Foundation.Naming;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>
/// The Module's own context over the identity schema: 11 account tables and OpenIddict's four <c>identity_oidc_*</c>
/// tables, mapped only. The embedded SQL migration set <see cref="IdentityDatabaseOptions.MigrationSetName"/> creates and
/// evolves them; EF never does.
/// </summary>
/// <remarks>
///   - names are pinned here, whatever the host's naming convention: snake_case columns, <c>pk_{table}</c> keys and
///     <c>ix_{table}_{columns}</c> indexes, matching <c>Migrations/{Postgres,Sqlite}/</c> column for column
///   - PostgreSQL qualifies every table with <see cref="IdentityDatabaseOptions.Schema"/>; SQLite has no schemas and
///     stores instants as binary <see cref="long"/> values, so range reads and ordering match PostgreSQL
///   - registered by <c>AddUserAccounts</c>; products never map these tables into their own context
///   - the OIDC tables exist in every form (one schema shape everywhere); only the Server package reads or writes them,
///     through OpenIddict's EF Core stores over this context
/// </remarks>
public sealed class IdentityDbContext : AppDbContextBase
{
    /// <summary>Create the context.</summary>
    /// <param name="options">The context options, configured by <c>AddUserAccounts</c>.</param>
    /// <param name="database">Where the schema lives; its <see cref="IdentityDatabaseOptions.Schema"/> qualifies PostgreSQL tables.</param>
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options, IdentityDatabaseOptions database)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(database);
        Schema = database.Schema;
    }

    /// <summary>The PostgreSQL schema this context's model qualifies its tables with.</summary>
    internal string Schema { get; }

    /// <summary>User accounts (<c>identity_users</c>).</summary>
    public DbSet<IdentityUser> Users => Set<IdentityUser>();

    /// <summary>Roles (<c>identity_roles</c>).</summary>
    public DbSet<IdentityRole> Roles => Set<IdentityRole>();

    /// <summary>User-role assignments (<c>identity_user_roles</c>).</summary>
    public DbSet<IdentityUserRole> UserRoles => Set<IdentityUserRole>();

    /// <summary>Claims held by users (<c>identity_user_claims</c>).</summary>
    public DbSet<IdentityUserClaim> UserClaims => Set<IdentityUserClaim>();

    /// <summary>Claims carried by roles (<c>identity_role_claims</c>).</summary>
    public DbSet<IdentityRoleClaim> RoleClaims => Set<IdentityRoleClaim>();

    /// <summary>External logins (<c>identity_user_logins</c>).</summary>
    public DbSet<IdentityUserLogin> UserLogins => Set<IdentityUserLogin>();

    /// <summary>Per-user stored tokens (<c>identity_user_tokens</c>).</summary>
    public DbSet<IdentityUserToken> UserTokens => Set<IdentityUserToken>();

    /// <summary>Refresh tokens by digest (<c>identity_refresh_tokens</c>).</summary>
    public DbSet<IdentityRefreshToken> RefreshTokens => Set<IdentityRefreshToken>();

    /// <summary>WebAuthn credentials (<c>identity_passkeys</c>).</summary>
    public DbSet<IdentityPasskey> Passkeys => Set<IdentityPasskey>();

    /// <summary>Organizations (<c>identity_organizations</c>).</summary>
    public DbSet<IdentityOrganization> Organizations => Set<IdentityOrganization>();

    /// <summary>Organization memberships (<c>identity_memberships</c>).</summary>
    public DbSet<IdentityMembership> Memberships => Set<IdentityMembership>();

    /// <summary>OpenID Connect clients (<c>identity_oidc_applications</c>).</summary>
    public DbSet<IdentityOidcApplication> OidcApplications => Set<IdentityOidcApplication>();

    /// <summary>OpenID Connect authorizations (<c>identity_oidc_authorizations</c>).</summary>
    public DbSet<IdentityOidcAuthorization> OidcAuthorizations => Set<IdentityOidcAuthorization>();

    /// <summary>OpenID Connect scopes (<c>identity_oidc_scopes</c>).</summary>
    public DbSet<IdentityOidcScope> OidcScopes => Set<IdentityOidcScope>();

    /// <summary>OpenID Connect tokens (<c>identity_oidc_tokens</c>).</summary>
    public DbSet<IdentityOidcToken> OidcTokens => Set<IdentityOidcToken>();

    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        // The model depends on the schema, so contexts over two schemas in one process never share a cached model.
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, IdentityModelCacheKeyFactory>();
        base.OnConfiguring(optionsBuilder);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        if (Database.IsNpgsql())
            modelBuilder.HasDefaultSchema(Schema);

        MapAccounts(modelBuilder);
        MapCredentials(modelBuilder);
        MapOrganizations(modelBuilder);
        MapOidc(modelBuilder);
        PinNames(modelBuilder);

        if (Database.IsSqlite())
            modelBuilder.ApplyDateTimeOffsetToBinaryConversion();
    }

    /// <summary>Maps users, roles and their relation, claim, login and token rows.</summary>
    private static void MapAccounts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityUser>(user =>
        {
            user.ToTable("identity_users");
            user.HasKey(u => u.Id);
            user.Property(u => u.Id).ValueGeneratedNever();
            user.HasIndex(u => u.NormalizedUserName).IsUnique();
            user.HasIndex(u => u.NormalizedEmail);
            user.HasIndex(u => u.PhoneNumber);
            user.Property(u => u.UserName).HasMaxLength(256);
            user.Property(u => u.NormalizedUserName).HasMaxLength(256);
            user.Property(u => u.Email).HasMaxLength(320);
            user.Property(u => u.NormalizedEmail).HasMaxLength(320);
            user.Property(u => u.PhoneNumber).HasMaxLength(32);
            user.Property(u => u.ConcurrencyStamp).IsConcurrencyToken();
        });

        modelBuilder.Entity<IdentityRole>(role =>
        {
            role.ToTable("identity_roles");
            role.HasKey(r => r.Id);
            role.Property(r => r.Id).ValueGeneratedNever();
            role.HasIndex(r => r.NormalizedName).IsUnique();
            role.Property(r => r.Name).HasMaxLength(256);
            role.Property(r => r.NormalizedName).HasMaxLength(256);
            role.Property(r => r.ConcurrencyStamp).IsConcurrencyToken();
        });

        modelBuilder.Entity<IdentityUserRole>(userRole =>
        {
            userRole.ToTable("identity_user_roles");
            userRole.HasKey(ur => new { ur.UserId, ur.RoleId });
        });

        modelBuilder.Entity<IdentityUserClaim>(userClaim =>
        {
            userClaim.ToTable("identity_user_claims");
            userClaim.HasKey(uc => uc.Id);
            userClaim.HasIndex(uc => uc.UserId);
        });

        modelBuilder.Entity<IdentityRoleClaim>(roleClaim =>
        {
            roleClaim.ToTable("identity_role_claims");
            roleClaim.HasKey(rc => rc.Id);
            roleClaim.HasIndex(rc => rc.RoleId);
        });

        modelBuilder.Entity<IdentityUserLogin>(login =>
        {
            login.ToTable("identity_user_logins");
            login.HasKey(l => new { l.LoginProvider, l.ProviderKey });
            login.Property(l => l.LoginProvider).HasMaxLength(128);
            login.Property(l => l.ProviderKey).HasMaxLength(128);
            login.HasIndex(l => l.UserId);
        });

        modelBuilder.Entity<IdentityUserToken>(token =>
        {
            token.ToTable("identity_user_tokens");
            token.HasKey(t => new { t.UserId, t.LoginProvider, t.Name });
            token.Property(t => t.LoginProvider).HasMaxLength(128);
            token.Property(t => t.Name).HasMaxLength(128);
        });
    }

    /// <summary>Maps refresh tokens and passkeys.</summary>
    private static void MapCredentials(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityRefreshToken>(refresh =>
        {
            refresh.ToTable("identity_refresh_tokens");
            refresh.HasKey(r => r.Id);
            refresh.Property(r => r.Id).ValueGeneratedNever();
            refresh.HasIndex(r => r.UserId);
            refresh.HasIndex(r => r.FamilyId);
            refresh.Property(r => r.TokenHash).HasMaxLength(64);
            refresh.Property(r => r.SecurityStamp).HasMaxLength(64);
        });

        modelBuilder.Entity<IdentityPasskey>(passkey =>
        {
            passkey.ToTable("identity_passkeys");
            passkey.HasKey(p => p.Id);
            passkey.Property(p => p.Id).ValueGeneratedNever();
            passkey.HasIndex(p => p.CredentialId).IsUnique();
            passkey.HasIndex(p => p.UserId);
            passkey.Property(p => p.CredentialId).HasMaxLength(1023);
            passkey.Property(p => p.Name).HasMaxLength(100);
        });
    }

    /// <summary>Maps organizations and memberships.</summary>
    private static void MapOrganizations(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityOrganization>(organization =>
        {
            organization.ToTable("identity_organizations");
            organization.HasKey(o => o.Id);
            organization.Property(o => o.Id).ValueGeneratedNever();
            organization.HasIndex(o => o.Slug).IsUnique();
            organization.Property(o => o.Name).HasMaxLength(120);
            organization.Property(o => o.Slug).HasMaxLength(64);
        });

        modelBuilder.Entity<IdentityMembership>(membership =>
        {
            membership.ToTable("identity_memberships");
            membership.HasKey(m => new { m.OrganizationId, m.UserId });
            membership.HasIndex(m => m.UserId);
            membership.Property(m => m.Role).HasMaxLength(64);
        });
    }

    /// <summary>
    /// Maps OpenIddict's application, authorization, scope and token entities as OpenIddict's own EF Core configuration
    /// does (lengths, indexes, concurrency tokens, the optional application and authorization links), under the
    /// <c>identity_oidc_</c> prefix, with ids made in code.
    /// </summary>
    /// <remarks>The links are model relationships only: like every identity table, the SQL declares no foreign keys.</remarks>
    private static void MapOidc(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityOidcApplication>(application =>
        {
            application.ToTable("identity_oidc_applications");
            application.HasKey(a => a.Id);
            application.Property(a => a.Id).ValueGeneratedNever();
            application.HasIndex(a => a.ClientId).IsUnique();
            application.Property(a => a.ApplicationType).HasMaxLength(50);
            application.Property(a => a.ClientId).HasMaxLength(100);
            application.Property(a => a.ClientType).HasMaxLength(50);
            application.Property(a => a.ConcurrencyToken).HasMaxLength(50).IsConcurrencyToken();
            application.Property(a => a.ConsentType).HasMaxLength(50);
            application.HasMany(a => a.Authorizations).WithOne(a => a.Application).HasForeignKey("ApplicationId").IsRequired(false);
            application.HasMany(a => a.Tokens).WithOne(t => t.Application).HasForeignKey("ApplicationId").IsRequired(false);
        });

        modelBuilder.Entity<IdentityOidcAuthorization>(authorization =>
        {
            authorization.ToTable("identity_oidc_authorizations");
            authorization.HasKey(a => a.Id);
            authorization.Property(a => a.Id).ValueGeneratedNever();
            authorization.Property(a => a.ConcurrencyToken).HasMaxLength(50).IsConcurrencyToken();
            authorization.Property(a => a.Status).HasMaxLength(50);
            authorization.Property(a => a.Subject).HasMaxLength(400);
            authorization.Property(a => a.Type).HasMaxLength(50);

            // The pinned name would pass PostgreSQL's 63-character identifier limit.
            authorization.HasIndex("ApplicationId", nameof(IdentityOidcAuthorization.Status), nameof(IdentityOidcAuthorization.Subject), nameof(IdentityOidcAuthorization.Type))
                .HasDatabaseName("ix_identity_oidc_authorizations_app_status_subject_type");
            authorization.HasMany(a => a.Tokens).WithOne(t => t.Authorization).HasForeignKey("AuthorizationId").IsRequired(false);
        });

        modelBuilder.Entity<IdentityOidcScope>(scope =>
        {
            scope.ToTable("identity_oidc_scopes");
            scope.HasKey(s => s.Id);
            scope.Property(s => s.Id).ValueGeneratedNever();
            scope.HasIndex(s => s.Name).IsUnique();
            scope.Property(s => s.ConcurrencyToken).HasMaxLength(50).IsConcurrencyToken();
            scope.Property(s => s.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<IdentityOidcToken>(token =>
        {
            token.ToTable("identity_oidc_tokens");
            token.HasKey(t => t.Id);
            token.Property(t => t.Id).ValueGeneratedNever();
            token.HasIndex(t => t.ReferenceId).IsUnique();
            token.HasIndex("ApplicationId", nameof(IdentityOidcToken.Status), nameof(IdentityOidcToken.Subject), nameof(IdentityOidcToken.Type));
            token.Property(t => t.ConcurrencyToken).HasMaxLength(50).IsConcurrencyToken();
            token.Property(t => t.ReferenceId).HasMaxLength(100);
            token.Property(t => t.Status).HasMaxLength(50);
            token.Property(t => t.Subject).HasMaxLength(400);
            token.Property(t => t.Type).HasMaxLength(150);
        });
    }

    /// <summary>
    /// Pins snake_case column, key and index names, so the host's naming convention never reaches the identity tables. An
    /// index already named in its mapping keeps that name.
    /// </summary>
    private static void PinNames(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var table = entity.GetTableName()!;
            foreach (var property in entity.GetProperties())
                property.SetColumnName(CaseMapper.ToSnakeCase(property.Name));

            entity.FindPrimaryKey()?.SetName($"pk_{table}");
            foreach (var index in entity.GetIndexes().Where(index => index.FindAnnotation(RelationalAnnotationNames.Name) is null))
                index.SetDatabaseName($"ix_{table}_{string.Join('_', index.Properties.Select(property => property.GetColumnName()))}");
        }
    }
}

/// <summary>Keys the cached <see cref="IdentityDbContext"/> model by schema as well as by context type.</summary>
internal sealed class IdentityModelCacheKeyFactory : IModelCacheKeyFactory
{
    /// <inheritdoc />
    public object Create(DbContext context, bool designTime)
        => context is IdentityDbContext identity
            ? (context.GetType(), identity.Schema, designTime)
            : (context.GetType(), designTime);
}

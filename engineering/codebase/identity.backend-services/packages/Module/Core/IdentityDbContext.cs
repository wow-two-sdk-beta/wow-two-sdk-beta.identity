using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Foundation.Naming;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>
/// The Module's own context over the identity schema in PostgreSQL: 11 account tables, OpenIddict's four
/// <c>identity_oidc_*</c> tables, the sessions, sign-in log and Data Protection key tables, the one-time code and
/// Telegram link and message tables, and the invitation, sign-up, picture and trusted contact tables, mapped only. The embedded
/// SQL migration set <see cref="IdentityDatabaseOptions.MigrationSetName"/> creates and evolves them; EF never does.
/// </summary>
/// <remarks>
///   - names are pinned here, whatever the host's naming convention: snake_case columns, <c>pk_{table}</c> keys and
///     <c>ix_{table}_{columns}</c> indexes, matching <c>Migrations/Postgres/</c> column for column
///   - every table is qualified with <see cref="IdentityDatabaseOptions.Schema"/>
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

    /// <summary>The partial-index predicate that scopes a user index to live accounts, as <c>001-accounts</c> builds it.</summary>
    internal const string LiveUserFilter = "deleted_at IS NULL";

    /// <summary>The longest display name <c>005-display-name</c> stores.</summary>
    public const int DisplayNameMaxLength = 120;

    /// <summary>The provider column of external accounts and stored tokens, as <c>001-accounts</c> names it.</summary>
    private const string ProviderColumn = "login_provider";

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

    /// <summary>External accounts linked to people (<c>identity_user_logins</c>).</summary>
    public DbSet<IdentityExternalAccount> ExternalAccounts => Set<IdentityExternalAccount>();

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

    /// <summary>Signed-in browsers and devices, one per sign-in (<c>identity_sessions</c>).</summary>
    public DbSet<IdentitySession> Sessions => Set<IdentitySession>();

    /// <summary>Every sign-in attempt (<c>identity_sign_in_log</c>).</summary>
    public DbSet<IdentitySignInLogEntry> SignInLog => Set<IdentitySignInLogEntry>();

    /// <summary>The instance's Data Protection key ring (<c>identity_data_protection_keys</c>).</summary>
    public DbSet<IdentityDataProtectionKey> DataProtectionKeys => Set<IdentityDataProtectionKey>();

    /// <summary>One-time codes as digests, shared by every instance (<c>identity_otp_codes</c>).</summary>
    public DbSet<IdentityOtpCode> OtpCodes => Set<IdentityOtpCode>();

    /// <summary>Phone numbers linked to the bot's Telegram chats (<c>identity_telegram_links</c>).</summary>
    public DbSet<IdentityTelegramLink> TelegramLinks => Set<IdentityTelegramLink>();

    /// <summary>Pending start links of the bot (<c>identity_telegram_link_requests</c>).</summary>
    public DbSet<IdentityTelegramLinkRequest> TelegramLinkRequests => Set<IdentityTelegramLinkRequest>();

    /// <summary>Telegram link changes waiting for an organization's owner or admin (<c>identity_telegram_link_changes</c>).</summary>
    public DbSet<IdentityTelegramLinkChange> TelegramLinkChanges => Set<IdentityTelegramLinkChange>();

    /// <summary>The bot's sent codes and notices waiting for cleanup (<c>identity_telegram_messages</c>).</summary>
    public DbSet<IdentityTelegramMessage> TelegramMessages => Set<IdentityTelegramMessage>();

    /// <summary>The inline buttons on those messages, by token digest (<c>identity_telegram_callbacks</c>).</summary>
    public DbSet<IdentityTelegramCallback> TelegramCallbacks => Set<IdentityTelegramCallback>();

    /// <summary>Invitations into organizations (<c>identity_invitations</c>).</summary>
    public DbSet<IdentityInvitation> Invitations => Set<IdentityInvitation>();

    /// <summary>Sign-ups browsers started from invitations (<c>identity_sign_up_requests</c>).</summary>
    public DbSet<IdentitySignUpRequest> SignUpRequests => Set<IdentitySignUpRequest>();

    /// <summary>People's profile pictures (<c>identity_profile_pictures</c>).</summary>
    public DbSet<IdentityProfilePicture> ProfilePictures => Set<IdentityProfilePicture>();

    /// <summary>Accounts' trusted contacts (<c>identity_trusted_contacts</c>).</summary>
    public DbSet<IdentityTrustedContact> TrustedContacts => Set<IdentityTrustedContact>();

    /// <summary>The browsers accounts may sign in from with no contact's press (<c>identity_known_devices</c>).</summary>
    public DbSet<IdentityKnownDevice> KnownDevices => Set<IdentityKnownDevice>();

    /// <summary>The sign-ins waiting for a contact's press (<c>identity_device_confirmations</c>).</summary>
    public DbSet<IdentityDeviceConfirmation> DeviceConfirmations => Set<IdentityDeviceConfirmation>();

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

        modelBuilder.HasDefaultSchema(Schema);
        MapAccounts(modelBuilder);
        MapCredentials(modelBuilder);
        MapOrganizations(modelBuilder);
        MapOidc(modelBuilder);
        MapSpine(modelBuilder);
        MapPhoneSignIn(modelBuilder);
        PinNames(modelBuilder);
    }

    /// <summary>Maps users, roles and their relation, claim, external-account and token rows.</summary>
    /// <remarks>
    /// <see cref="IdentityUser"/> is <c>ISoftDeletable</c>: the base context's conventions hide rows whose
    /// <c>deleted_at</c> is set from every query, and the user-name and email indexes cover live rows only (<c>001</c>).
    /// </remarks>
    private static void MapAccounts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityUser>(user =>
        {
            user.ToTable("identity_users");
            user.HasKey(u => u.Id);
            user.Property(u => u.Id).ValueGeneratedNever();
            user.HasIndex(u => u.NormalizedUserName).IsUnique().HasFilter(LiveUserFilter);
            user.HasIndex(u => u.NormalizedEmail).HasFilter(LiveUserFilter);
            user.HasIndex(u => u.PhoneNumber);
            user.Property(u => u.UserName).HasMaxLength(256);
            user.Property(u => u.DisplayName).HasMaxLength(DisplayNameMaxLength);
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

        // The table and its provider column keep their 001 names; the members say external account and provider.
        modelBuilder.Entity<IdentityExternalAccount>(account =>
        {
            account.ToTable("identity_user_logins");
            account.HasKey(a => new { a.Provider, a.ProviderKey });
            account.Property(a => a.Provider).HasMaxLength(128).HasColumnName(ProviderColumn);
            account.Property(a => a.ProviderKey).HasMaxLength(128);
            account.HasIndex(a => a.UserId);
        });

        modelBuilder.Entity<IdentityUserToken>(token =>
        {
            token.ToTable("identity_user_tokens");
            token.HasKey(t => new { t.UserId, t.Provider, t.Name });
            token.Property(t => t.Provider).HasMaxLength(128).HasColumnName(ProviderColumn);
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

    /// <summary>Maps organizations, memberships and invitations.</summary>
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

        modelBuilder.Entity<IdentityInvitation>(invitation =>
        {
            invitation.ToTable("identity_invitations");
            invitation.HasKey(i => i.Id);
            invitation.Property(i => i.Id).ValueGeneratedNever();
            invitation.HasIndex(i => i.TokenHash).IsUnique();
            invitation.HasIndex(i => i.OrganizationId);
            invitation.Property(i => i.Role).HasMaxLength(64);
            invitation.Property(i => i.TokenHash).HasMaxLength(64);
            invitation.Property(i => i.PhoneNumber).HasMaxLength(32);
            invitation.Property(i => i.Email).HasMaxLength(256);
        });

        // 010-trusted-contacts: one row per account and contact.
        modelBuilder.Entity<IdentityTrustedContact>(contact =>
        {
            contact.ToTable("identity_trusted_contacts");
            contact.HasKey(c => new { c.UserId, c.ContactUserId });
            contact.HasIndex(c => c.ContactUserId);
        });

        // 011-device-confirmations: known browsers, and sign-ins waiting for a contact's press.
        modelBuilder.Entity<IdentityKnownDevice>(device =>
        {
            device.ToTable("identity_known_devices");
            device.HasKey(d => new { d.UserId, d.DeviceHash });
            device.Property(d => d.DeviceHash).HasMaxLength(64);
        });

        modelBuilder.Entity<IdentityDeviceConfirmation>(confirmation =>
        {
            confirmation.ToTable("identity_device_confirmations");
            confirmation.HasKey(c => c.Id);
            confirmation.Property(c => c.Id).ValueGeneratedNever();
            confirmation.HasIndex(c => c.TokenHash).IsUnique();
            confirmation.HasIndex(c => c.UserId);
            confirmation.HasIndex(c => c.ExpiresAt);
            confirmation.Property(c => c.DeviceHash).HasMaxLength(64);
            confirmation.Property(c => c.TokenHash).HasMaxLength(64);
            confirmation.Property(c => c.IpMasked).HasMaxLength(64);
            confirmation.Property(c => c.UserAgent).HasMaxLength(256);
        });

        modelBuilder.Entity<IdentitySignUpRequest>(request =>
        {
            request.ToTable("identity_sign_up_requests");
            request.HasKey(r => r.Id);
            request.Property(r => r.Id).ValueGeneratedNever();
            request.HasIndex(r => r.ExpiresAt);
            request.Property(r => r.DisplayName).HasMaxLength(DisplayNameMaxLength);
            request.Property(r => r.BrowserSecretHash).HasMaxLength(64);
            request.Property(r => r.ClientId).HasMaxLength(100);
            request.Property(r => r.FailureCode).HasMaxLength(64);
        });

        modelBuilder.Entity<IdentityProfilePicture>(picture =>
        {
            picture.ToTable("identity_profile_pictures");
            picture.HasKey(p => p.UserId);
            picture.Property(p => p.UserId).ValueGeneratedNever();
            picture.Property(p => p.ContentType).HasMaxLength(64);
            picture.Property(p => p.Version).HasMaxLength(64);
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

    /// <summary>Maps sessions, the sign-in log and the Data Protection key ring (<c>003-spine</c>).</summary>
    private static void MapSpine(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentitySession>(session =>
        {
            session.ToTable("identity_sessions");
            session.HasKey(s => s.Id);
            session.Property(s => s.Id).ValueGeneratedNever();
            session.HasIndex(s => s.UserId);
            session.Property(s => s.SecurityStamp).HasMaxLength(64);
            session.Property(s => s.RevokeReason).HasMaxLength(32);
            session.Property(s => s.IpMasked).HasMaxLength(64);
            session.Property(s => s.UserAgent).HasMaxLength(256);
        });

        modelBuilder.Entity<IdentitySignInLogEntry>(entry =>
        {
            entry.ToTable("identity_sign_in_log");
            entry.HasKey(e => e.Id);
            entry.HasIndex(e => e.OccurredAt);
            entry.HasIndex(e => e.UserId);
            entry.Property(e => e.SubjectMasked).HasMaxLength(100);
            entry.Property(e => e.Method).HasMaxLength(32);
            entry.Property(e => e.ClientId).HasMaxLength(100);
            entry.Property(e => e.Result).HasMaxLength(32);
            entry.Property(e => e.Reason).HasMaxLength(64);
            entry.Property(e => e.IpMasked).HasMaxLength(64);
            entry.Property(e => e.UserAgent).HasMaxLength(256);
            entry.Property(e => e.TraceId).HasMaxLength(32);
        });

        modelBuilder.Entity<IdentityDataProtectionKey>(key =>
        {
            key.ToTable("identity_data_protection_keys");
            key.HasKey(k => k.Id);
            key.Property(k => k.Id).ValueGeneratedNever();
        });
    }

    /// <summary>
    /// Maps one-time codes, Telegram links and the bot's start links (<c>004-phone-sign-in</c>), the pending link changes
    /// (<c>008-telegram-link-changes</c>), and the bot's message ledger and its buttons (<c>009-telegram-messages</c>).
    /// </summary>
    private static void MapPhoneSignIn(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityOtpCode>(code =>
        {
            code.ToTable("identity_otp_codes");
            code.HasKey(c => c.Id);
            code.Property(c => c.Id).ValueGeneratedNever();
            code.HasIndex(c => new { c.SubjectHash, c.Scope });
            code.HasIndex(c => new { c.OriginHash, c.Scope });
            code.HasIndex(c => c.ExpiresAt);
            code.Property(c => c.SubjectHash).HasMaxLength(64);
            code.Property(c => c.Scope).HasMaxLength(100);
            code.Property(c => c.CodeHash).HasMaxLength(64);
            code.Property(c => c.OriginHash).HasMaxLength(64);
        });

        modelBuilder.Entity<IdentityTelegramLink>(link =>
        {
            link.ToTable("identity_telegram_links");
            link.HasKey(l => l.Id);
            link.Property(l => l.Id).ValueGeneratedNever();
            link.HasIndex(l => new { l.BotUserName, l.PhoneNumber }).IsUnique();
            link.Property(l => l.BotUserName).HasMaxLength(64);
            link.Property(l => l.PhoneNumber).HasMaxLength(32);
        });

        modelBuilder.Entity<IdentityTelegramLinkRequest>(request =>
        {
            request.ToTable("identity_telegram_link_requests");
            request.HasKey(r => r.Id);
            request.Property(r => r.Id).ValueGeneratedNever();
            request.HasIndex(r => r.TokenHash).IsUnique();
            request.HasIndex(r => r.TelegramUserId);
            request.HasIndex(r => r.PhoneNumber);
            request.HasIndex(r => r.OriginHash);
            request.HasIndex(r => r.ExpiresAt);
            request.Property(r => r.TokenHash).HasMaxLength(64);
            request.Property(r => r.Purpose).HasMaxLength(64);
            request.Property(r => r.BotUserName).HasMaxLength(64);
            request.Property(r => r.PhoneNumber).HasMaxLength(32);
            request.Property(r => r.OriginHash).HasMaxLength(64);
        });

        // 008-telegram-link-changes: one pending change per bot and number.
        modelBuilder.Entity<IdentityTelegramLinkChange>(change =>
        {
            change.ToTable("identity_telegram_link_changes");
            change.HasKey(c => c.Id);
            change.Property(c => c.Id).ValueGeneratedNever();
            change.HasIndex(c => new { c.BotUserName, c.PhoneNumber }).IsUnique();
            change.HasIndex(c => c.RequestedAt);
            change.Property(c => c.BotUserName).HasMaxLength(64);
            change.Property(c => c.PhoneNumber).HasMaxLength(32);
        });

        // 009-telegram-messages: the sent codes and notices the sweep cleans up, and their buttons.
        modelBuilder.Entity<IdentityTelegramMessage>(message =>
        {
            message.ToTable("identity_telegram_messages");
            message.HasKey(m => m.Id);
            message.Property(m => m.Id).ValueGeneratedNever();
            message.HasIndex(m => new { m.BotUserName, m.ChatId, m.MessageId }).IsUnique();
            message.HasIndex(m => new { m.UserId, m.Kind });
            message.HasIndex(m => m.DeleteAt);
            message.Property(m => m.BotUserName).HasMaxLength(64);
            message.Property(m => m.Kind).HasMaxLength(64);
            message.Property(m => m.Culture).HasMaxLength(16);
        });

        modelBuilder.Entity<IdentityTelegramCallback>(callback =>
        {
            callback.ToTable("identity_telegram_callbacks");
            callback.HasKey(c => c.Id);
            callback.Property(c => c.Id).ValueGeneratedNever();
            callback.HasIndex(c => c.TokenHash).IsUnique();
            callback.HasIndex(c => c.MessageRecordId);
            callback.Property(c => c.TokenHash).HasMaxLength(64);
            callback.Property(c => c.Action).HasMaxLength(64);
            callback.Property(c => c.Payload).HasMaxLength(256);
        });
    }

    /// <summary>
    /// Pins snake_case column, key and index names, so the host's naming convention never reaches the identity tables. A
    /// column or index already named in its mapping keeps that name.
    /// </summary>
    private static void PinNames(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var table = entity.GetTableName()!;
            foreach (var property in entity.GetProperties().Where(property => property.FindAnnotation(RelationalAnnotationNames.ColumnName) is null))
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

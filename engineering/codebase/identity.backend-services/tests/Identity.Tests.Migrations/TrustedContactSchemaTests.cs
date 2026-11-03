using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace Identity.Tests.Migrations;

/// <summary>
/// <c>010-trusted-contacts</c> on its own: rollback → apply over the rest keeping existing rows, a contact and the three
/// contact columns round-tripping through <see cref="IdentityDbContext"/>, and one row per account and contact.
/// </summary>
/// <param name="fixture">The shared container.</param>
[Collection(PostgresMigratorCollection.Name)]
public sealed class TrustedContactSchemaTests(MigratorPostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset At = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public async Task InitializeAsync() => await fixture.ResetAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RollbackApply_ShouldDropAndRecreateTheContacts_AndKeepTheRowsTheColumnsSitOn()
    {
        await using var migrator = IdentitySchemaTests.CreateMigrator(fixture);
        var runner = migrator.RunnerFor(IdentityDatabaseOptions.MigrationSetName);
        (await migrator.ApplyAllAsync()).ValueOrThrow();
        await using (var connection = await migrator.OpenConnectionAsync())
        {
            await using var insert = new NpgsqlCommand(
                "insert into identity.identity_sessions (id, user_id, authentication_methods, created_at, last_seen_at, expires_at, trusted_contact_user_id) values (@id, @user, '{otp}', @at, @at, @at, @contact)",
                (NpgsqlConnection)connection);
            insert.Parameters.AddWithValue("id", Guid.CreateVersion7(At));
            insert.Parameters.AddWithValue("user", Guid.CreateVersion7(At));
            insert.Parameters.AddWithValue("at", At);
            insert.Parameters.AddWithValue("contact", Guid.CreateVersion7(At));
            await insert.ExecuteNonQueryAsync();
        }

        (await runner.RollbackAsync(targetOrdinal: 9)).ValueOrThrow().Should().Equal("010-trusted-contacts");
        (await migrator.HasTableAsync(IdentitySchemaTests.Schema, "identity_trusted_contacts")).Should().BeFalse();
        foreach (var table in new[] { "identity_invitations", "identity_sessions", "identity_sign_in_log" })
            (await IdentitySchemaTests.HasColumnAsync(migrator, table, "trusted_contact_user_id")).Should().BeFalse($"the rollback drops {table}'s contact");

        (await migrator.ApplyAllAsync()).ValueOrThrow().Single().Applied.Should().Equal("010-trusted-contacts");
        (await migrator.HasTableAsync(IdentitySchemaTests.Schema, "identity_trusted_contacts")).Should().BeTrue();
        await using var check = await migrator.OpenConnectionAsync();
        await using var count = new NpgsqlCommand("select count(*) from identity.identity_sessions where trusted_contact_user_id is null", (NpgsqlConnection)check);
        ((long)(await count.ExecuteScalarAsync())!).Should().Be(1, "the session row stays, its contact column back empty");
    }

    [Fact]
    public async Task Contacts_ShouldRoundTrip_HoldOnePerPair_AndTheColumnsShouldMapTheirContact()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDatabaseSettings(fixture.ConnectionString);
        services.AddUserAccounts();
        await using var provider = services.BuildServiceProvider();
        (await provider.ApplyMigrationSetsAsync("test")).ValueOrThrow();
        var account = Guid.CreateVersion7(At);
        var contact = Guid.CreateVersion7(At.AddSeconds(1));
        var trusted = new IdentityTrustedContact { UserId = account, ContactUserId = contact, CreatedByUserId = contact, CreatedAt = At };
        var session = new IdentitySession
        {
            Id = Guid.CreateVersion7(At),
            UserId = account,
            AuthenticationMethods = ["otp"],
            CreatedAt = At,
            LastSeenAt = At,
            ExpiresAt = At.AddHours(12),
            TrustedContactUserId = contact,
        };
        var entry = new IdentitySignInLogEntry { OccurredAt = At, UserId = account, Method = "trusted_contact_code", Result = "succeeded", SessionId = session.Id, TrustedContactUserId = contact };
        var invitation = new IdentityInvitation
        {
            Id = Guid.CreateVersion7(At),
            OrganizationId = Guid.CreateVersion7(At),
            Role = "owner",
            TokenHash = new string('b', 64),
            CreatedByUserId = contact,
            CreatedAt = At,
            ExpiresAt = At.AddDays(7),
            TrustedContactUserId = contact,
        };
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            context.TrustedContacts.Add(trusted);
            context.Sessions.Add(session);
            context.SignInLog.Add(entry);
            context.Invitations.Add(invitation);
            await context.SaveChangesAsync();
        }

        await using (var read = provider.CreateAsyncScope())
        {
            var context = read.ServiceProvider.GetRequiredService<IdentityDbContext>();
            (await context.TrustedContacts.SingleAsync()).Should().BeEquivalentTo(trusted);
            (await context.Sessions.SingleAsync()).TrustedContactUserId.Should().Be(contact);
            (await context.SignInLog.SingleAsync()).TrustedContactUserId.Should().Be(contact);
            (await context.Invitations.SingleAsync()).TrustedContactUserId.Should().Be(contact);
        }

        await using var duplicate = provider.CreateAsyncScope();
        var again = duplicate.ServiceProvider.GetRequiredService<IdentityDbContext>();
        again.TrustedContacts.Add(new IdentityTrustedContact { UserId = account, ContactUserId = contact, CreatedByUserId = account, CreatedAt = At.AddDays(1) });
        var save = () => again.SaveChangesAsync();
        (await save.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }
}

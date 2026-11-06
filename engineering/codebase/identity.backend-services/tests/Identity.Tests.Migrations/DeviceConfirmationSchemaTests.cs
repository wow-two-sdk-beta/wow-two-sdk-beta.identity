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
/// <c>011-device-confirmations</c> and <c>012-device-confirmation-request</c> on their own: rollback → apply over the rest, a known
/// browser and a request round-tripping through <see cref="IdentityDbContext"/>, one row per account and browser, one request per
/// token, and a request waiting across <c>012</c> counting as a browser's.
/// </summary>
/// <param name="fixture">The shared container.</param>
[Collection(PostgresMigratorCollection.Name)]
public sealed class DeviceConfirmationSchemaTests(MigratorPostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset At = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public async Task InitializeAsync() => await fixture.ResetAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RollbackApply_ShouldDropAndRecreateTheTables_AndLeaveTheContactsAlone()
    {
        await using var migrator = IdentitySchemaTests.CreateMigrator(fixture);
        var runner = migrator.RunnerFor(IdentityDatabaseOptions.MigrationSetName);
        (await migrator.ApplyAllAsync()).ValueOrThrow();
        await using (var connection = await migrator.OpenConnectionAsync())
        {
            await using var insert = new NpgsqlCommand(
                "insert into identity.identity_trusted_contacts (user_id, contact_user_id, created_by_user_id, created_at) values (@user, @contact, @contact, @at)",
                (NpgsqlConnection)connection);
            insert.Parameters.AddWithValue("user", Guid.CreateVersion7(At));
            insert.Parameters.AddWithValue("contact", Guid.CreateVersion7(At));
            insert.Parameters.AddWithValue("at", At);
            await insert.ExecuteNonQueryAsync();
        }

        (await runner.RollbackAsync(targetOrdinal: 10)).ValueOrThrow().Should().Equal("012-device-confirmation-request", "011-device-confirmations");
        foreach (var table in SchemaShape.DeviceConfirmationTables)
            (await migrator.HasTableAsync(IdentitySchemaTests.Schema, table)).Should().BeFalse();

        (await migrator.ApplyAllAsync()).ValueOrThrow().Single().Applied.Should().Equal("011-device-confirmations", "012-device-confirmation-request");
        foreach (var table in SchemaShape.DeviceConfirmationTables)
            (await migrator.HasTableAsync(IdentitySchemaTests.Schema, table)).Should().BeTrue();

        await using var check = await migrator.OpenConnectionAsync();
        await using var count = new NpgsqlCommand("select count(*) from identity.identity_trusted_contacts", (NpgsqlConnection)check);
        ((long)(await count.ExecuteScalarAsync())!).Should().Be(1, "the contact row stays");
    }

    [Fact]
    public async Task RollbackApply012_ShouldKeepAWaitingRequest_CountingAsABrowsers()
    {
        await using var migrator = IdentitySchemaTests.CreateMigrator(fixture);
        var runner = migrator.RunnerFor(IdentityDatabaseOptions.MigrationSetName);
        (await migrator.ApplyAllAsync()).ValueOrThrow();
        (await runner.RollbackAsync(targetOrdinal: 11)).ValueOrThrow().Should().Equal("012-device-confirmation-request");
        await using (var connection = await migrator.OpenConnectionAsync())
        {
            await using var insert = new NpgsqlCommand(
                "insert into identity.identity_device_confirmations (id, user_id, device_hash, token_hash, created_at, expires_at) values (@id, @user, @device, @token, @at, @at)",
                (NpgsqlConnection)connection);
            insert.Parameters.AddWithValue("id", Guid.CreateVersion7(At));
            insert.Parameters.AddWithValue("user", Guid.CreateVersion7(At));
            insert.Parameters.AddWithValue("device", new string('b', 64));
            insert.Parameters.AddWithValue("token", new string('c', 64));
            insert.Parameters.AddWithValue("at", At);
            await insert.ExecuteNonQueryAsync();
        }

        (await migrator.ApplyAllAsync()).ValueOrThrow().Single().Applied.Should().Equal("012-device-confirmation-request");

        await using var check = await migrator.OpenConnectionAsync();
        await using var read = new NpgsqlCommand("select is_cookie_client from identity.identity_device_confirmations", (NpgsqlConnection)check);
        ((bool)(await read.ExecuteScalarAsync())!).Should().BeTrue();
    }

    [Fact]
    public async Task Rows_ShouldRoundTrip_AndHoldOnePerBrowserAndOnePerToken()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDatabaseSettings(fixture.ConnectionString);
        services.AddUserAccounts();
        await using var provider = services.BuildServiceProvider();
        (await provider.ApplyMigrationSetsAsync("test")).ValueOrThrow();
        var account = Guid.CreateVersion7(At);
        var contact = Guid.CreateVersion7(At.AddSeconds(1));
        var device = new IdentityKnownDevice { UserId = account, DeviceHash = new string('a', 64), ConfirmedByUserId = contact, CreatedAt = At };
        var confirmation = new IdentityDeviceConfirmation
        {
            Id = Guid.CreateVersion7(At),
            UserId = account,
            DeviceHash = new string('b', 64),
            TokenHash = new string('c', 64),
            IsCookieClient = true,
            IpMasked = "203.0.113.0",
            UserAgent = "Mozilla/5.0",
            IsApproved = true,
            DecidedByUserId = contact,
            DecidedAt = At.AddMinutes(1),
            CreatedAt = At,
            ExpiresAt = At.AddMinutes(10),
        };
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            context.KnownDevices.Add(device);
            context.DeviceConfirmations.Add(confirmation);
            await context.SaveChangesAsync();
        }

        await using (var read = provider.CreateAsyncScope())
        {
            var context = read.ServiceProvider.GetRequiredService<IdentityDbContext>();
            (await context.KnownDevices.SingleAsync()).Should().BeEquivalentTo(device);
            (await context.DeviceConfirmations.SingleAsync()).Should().BeEquivalentTo(confirmation);
        }

        await using (var again = provider.CreateAsyncScope())
        {
            var context = again.ServiceProvider.GetRequiredService<IdentityDbContext>();
            context.KnownDevices.Add(new IdentityKnownDevice { UserId = account, DeviceHash = device.DeviceHash, ConfirmedByUserId = contact, CreatedAt = At });
            await FluentActions.Awaiting(() => context.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>("one row per account and browser");
        }

        await using var twin = provider.CreateAsyncScope();
        var twinContext = twin.ServiceProvider.GetRequiredService<IdentityDbContext>();
        twinContext.DeviceConfirmations.Add(new IdentityDeviceConfirmation
        {
            Id = Guid.CreateVersion7(At.AddSeconds(2)),
            UserId = account,
            DeviceHash = new string('b', 64),
            TokenHash = confirmation.TokenHash,
            CreatedAt = At,
            ExpiresAt = At.AddMinutes(10),
        });
        await FluentActions.Awaiting(() => twinContext.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>("one request per token");
    }
}

using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Notices;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace Identity.Tests.Migrations;

/// <summary>
/// <c>013-app-notices</c> on its own: rollback → apply over the rest, with a kept notice dropped by the rollback and a cleaned-up
/// one kept; a request and a mute round-tripping through <see cref="IdentityDbContext"/>; one request per app and key, one mute
/// per person and app.
/// </summary>
/// <param name="fixture">The shared container.</param>
[Collection(PostgresMigratorCollection.Name)]
public sealed class AppNoticeSchemaTests(MigratorPostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset At = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public async Task InitializeAsync() => await fixture.ResetAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RollbackApply_ShouldDropTheTablesAndKeptNotices_AndKeepTheRestOfTheLedger()
    {
        await using var migrator = IdentitySchemaTests.CreateMigrator(fixture);
        var runner = migrator.RunnerFor(IdentityDatabaseOptions.MigrationSetName);
        (await migrator.ApplyAllAsync()).ValueOrThrow();
        await using (var connection = await migrator.OpenConnectionAsync())
        {
            foreach (var (id, deleteAt) in new[] { (Guid.CreateVersion7(At), (DateTimeOffset?)null), (Guid.CreateVersion7(At.AddSeconds(1)), At.AddDays(1)) })
            {
                await using var insert = new NpgsqlCommand(
                    "insert into identity.identity_telegram_messages (id, bot_user_name, chat_id, message_id, kind, sent_at, delete_at) values (@id, 'ocharo_bot', 7, @message, 'app-notice', @at, @delete)",
                    (NpgsqlConnection)connection);
                insert.Parameters.AddWithValue("id", id);
                insert.Parameters.AddWithValue("message", deleteAt is null ? 1 : 2);
                insert.Parameters.AddWithValue("at", At);
                insert.Parameters.Add(new NpgsqlParameter("delete", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = (object?)deleteAt ?? DBNull.Value });
                await insert.ExecuteNonQueryAsync();
            }
        }

        (await runner.RollbackAsync(targetOrdinal: 12)).ValueOrThrow().Should().Equal("013-app-notices");
        foreach (var table in SchemaShape.AppNoticeTables)
            (await migrator.HasTableAsync(IdentitySchemaTests.Schema, table)).Should().BeFalse($"the rollback drops {table}");
        await using (var check = await migrator.OpenConnectionAsync())
        {
            await using var count = new NpgsqlCommand("select count(*) from identity.identity_telegram_messages", (NpgsqlConnection)check);
            ((long)(await count.ExecuteScalarAsync())!).Should().Be(1, "the kept notice goes, the one waiting for the sweep stays");
            await using var nullable = new NpgsqlCommand(
                "select is_nullable from information_schema.columns where table_schema = 'identity' and table_name = 'identity_telegram_messages' and column_name = 'delete_at'",
                (NpgsqlConnection)check);
            ((string)(await nullable.ExecuteScalarAsync())!).Should().Be("NO", "the rollback restores delete_at's NOT NULL");
        }

        (await migrator.ApplyAllAsync()).ValueOrThrow().Single().Applied.Should().Equal("013-app-notices");
        foreach (var table in SchemaShape.AppNoticeTables)
            (await migrator.HasTableAsync(IdentitySchemaTests.Schema, table)).Should().BeTrue($"the second apply recreates {table}");
    }

    [Fact]
    public async Task Rows_ShouldRoundTrip_AndHoldOneRequestPerKeyAndOneMutePerApp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDatabaseSettings(fixture.ConnectionString);
        services.AddUserAccounts();
        await using var provider = services.BuildServiceProvider();
        (await provider.ApplyMigrationSetsAsync("test")).ValueOrThrow();
        var person = Guid.CreateVersion7(At);
        var request = new IdentityAppNotice
        {
            Id = Guid.CreateVersion7(At),
            ClientId = "ocharo-events",
            IdempotencyKey = "digest-1:person-1",
            RequestHash = new string('a', 64),
            UserId = person,
            Status = AppNoticeStatusConstants.Sent,
            MessageRecordId = Guid.CreateVersion7(At),
            CreatedAt = At,
        };
        var mute = new IdentityNoticeMute { UserId = person, ClientId = IdentityNoticeMute.AllClients, MutedAt = At };
        var kept = new IdentityTelegramMessage
        {
            Id = Guid.CreateVersion7(At),
            BotUserName = "ocharo_bot",
            ChatId = 7,
            MessageId = 1,
            Kind = AppNoticeService.Kind,
            UserId = person,
            SentAt = At,
            DeleteAt = null,
        };
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            context.AppNotices.Add(request);
            context.NoticeMutes.Add(mute);
            context.TelegramMessages.Add(kept);
            await context.SaveChangesAsync();
        }

        await using (var read = provider.CreateAsyncScope())
        {
            var context = read.ServiceProvider.GetRequiredService<IdentityDbContext>();
            (await context.AppNotices.SingleAsync()).Should().BeEquivalentTo(request);
            (await context.NoticeMutes.SingleAsync()).Should().BeEquivalentTo(mute);
            (await context.TelegramMessages.SingleAsync()).DeleteAt.Should().BeNull("a kept notice has no cleanup time");
        }

        await using (var duplicate = provider.CreateAsyncScope())
        {
            var again = duplicate.ServiceProvider.GetRequiredService<IdentityDbContext>();
            again.AppNotices.Add(new IdentityAppNotice { Id = Guid.CreateVersion7(At), ClientId = "ocharo-events", IdempotencyKey = "digest-1:person-1", RequestHash = new string('b', 64), UserId = person, CreatedAt = At });
            var save = () => again.SaveChangesAsync();
            (await save.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        }

        await using var twice = provider.CreateAsyncScope();
        var other = twice.ServiceProvider.GetRequiredService<IdentityDbContext>();
        other.NoticeMutes.Add(new IdentityNoticeMute { UserId = person, ClientId = IdentityNoticeMute.AllClients, MutedAt = At.AddHours(1) });
        var saveMute = () => other.SaveChangesAsync();
        (await saveMute.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }
}

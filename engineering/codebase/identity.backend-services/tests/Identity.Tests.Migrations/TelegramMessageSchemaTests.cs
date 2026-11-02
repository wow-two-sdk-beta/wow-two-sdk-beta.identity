using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

namespace Identity.Tests.Migrations;

/// <summary>
/// <c>009-telegram-messages</c> on its own: rollback → apply over the rest, a message and its button round-tripping through
/// <see cref="IdentityDbContext"/>, one row per bot, chat and message id, and one button per token digest.
/// </summary>
/// <param name="fixture">The shared container.</param>
[Collection(PostgresMigratorCollection.Name)]
public sealed class TelegramMessageSchemaTests(MigratorPostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset At = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public async Task InitializeAsync() => await fixture.ResetAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RollbackApply_ShouldDropAndRecreateTheLedger_AndKeepTheLinkChanges()
    {
        await using var migrator = IdentitySchemaTests.CreateMigrator(fixture);
        var runner = migrator.RunnerFor(IdentityDatabaseOptions.MigrationSetName);
        (await migrator.ApplyAllAsync()).ValueOrThrow();

        (await runner.RollbackAsync(targetOrdinal: 8)).ValueOrThrow().Should().Equal("009-telegram-messages");
        foreach (var table in SchemaShape.TelegramMessageTables)
            (await migrator.HasTableAsync(IdentitySchemaTests.Schema, table)).Should().BeFalse($"the rollback drops {table}");
        (await migrator.HasTableAsync(IdentitySchemaTests.Schema, "identity_telegram_link_changes")).Should().BeTrue("008-telegram-link-changes stays");

        (await migrator.ApplyAllAsync()).ValueOrThrow().Single().Applied.Should().Equal("009-telegram-messages");
        foreach (var table in SchemaShape.TelegramMessageTables)
            (await migrator.HasTableAsync(IdentitySchemaTests.Schema, table)).Should().BeTrue($"the second apply recreates {table}");
    }

    [Fact]
    public async Task Ledger_ShouldRoundTrip_AndHoldOneRowPerMessageAndOneButtonPerToken()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDatabaseSettings(fixture.ConnectionString);
        services.AddUserAccounts();
        await using var provider = services.BuildServiceProvider();
        (await provider.ApplyMigrationSetsAsync("test")).ValueOrThrow();
        var message = new IdentityTelegramMessage
        {
            Id = Guid.CreateVersion7(At),
            BotUserName = "ocharo_bot",
            ChatId = 2002,
            MessageId = 41,
            Kind = "contact-sign-in",
            UserId = Guid.CreateVersion7(At),
            Culture = "uz",
            SentAt = At,
            DeleteAt = At.AddDays(1),
        };
        var callback = new IdentityTelegramCallback
        {
            Id = Guid.CreateVersion7(At),
            MessageRecordId = message.Id,
            TokenHash = new string('a', 64),
            Action = "trusted-contacts.revoke",
            Payload = "01900000-0000-7000-8000-000000000001",
            AllowedTelegramUserId = 2002,
            UsedAt = At.AddHours(1),
        };
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            context.TelegramMessages.Add(message);
            context.TelegramCallbacks.Add(callback);
            await context.SaveChangesAsync();
        }

        await using (var read = provider.CreateAsyncScope())
        {
            var context = read.ServiceProvider.GetRequiredService<IdentityDbContext>();
            (await context.TelegramMessages.SingleAsync()).Should().BeEquivalentTo(message);
            (await context.TelegramCallbacks.SingleAsync()).Should().BeEquivalentTo(callback);
        }

        await using (var duplicate = provider.CreateAsyncScope())
        {
            var again = duplicate.ServiceProvider.GetRequiredService<IdentityDbContext>();
            again.TelegramMessages.Add(new IdentityTelegramMessage { Id = Guid.CreateVersion7(At), BotUserName = "ocharo_bot", ChatId = 2002, MessageId = 41, Kind = "code", SentAt = At, DeleteAt = At });
            var save = () => again.SaveChangesAsync();
            (await save.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        }

        await using var token = provider.CreateAsyncScope();
        var other = token.ServiceProvider.GetRequiredService<IdentityDbContext>();
        other.TelegramCallbacks.Add(new IdentityTelegramCallback { Id = Guid.CreateVersion7(At), MessageRecordId = message.Id, TokenHash = new string('a', 64), Action = "x", AllowedTelegramUserId = 1 });
        var saveToken = () => other.SaveChangesAsync();
        (await saveToken.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }
}

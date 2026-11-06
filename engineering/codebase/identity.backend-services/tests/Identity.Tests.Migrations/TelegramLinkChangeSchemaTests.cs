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
/// <c>008-telegram-link-changes</c> on its own: rollback → apply over the rest, its row round-tripping through
/// <see cref="IdentityDbContext"/>, and one pending change per bot and number.
/// </summary>
/// <param name="fixture">The shared container.</param>
[Collection(PostgresMigratorCollection.Name)]
public sealed class TelegramLinkChangeSchemaTests(MigratorPostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset At = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public async Task InitializeAsync() => await fixture.ResetAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RollbackApply_ShouldDropAndRecreateTheChanges_AndKeepTheLinks()
    {
        await using var migrator = IdentitySchemaTests.CreateMigrator(fixture);
        var runner = migrator.RunnerFor(IdentityDatabaseOptions.MigrationSetName);
        (await migrator.ApplyAllAsync()).ValueOrThrow();

        (await runner.RollbackAsync(targetOrdinal: 7)).ValueOrThrow().Should().Equal("012-device-confirmation-request", "011-device-confirmations", "010-trusted-contacts", "009-telegram-messages", "008-telegram-link-changes");
        (await migrator.HasTableAsync(IdentitySchemaTests.Schema, "identity_telegram_link_changes")).Should().BeFalse();
        foreach (var table in SchemaShape.PhoneSignInTables)
            (await migrator.HasTableAsync(IdentitySchemaTests.Schema, table)).Should().BeTrue($"004-phone-sign-in keeps {table}");

        (await migrator.ApplyAllAsync()).ValueOrThrow().Single().Applied.Should().Equal("008-telegram-link-changes", "009-telegram-messages", "010-trusted-contacts", "011-device-confirmations", "012-device-confirmation-request");
        (await migrator.HasTableAsync(IdentitySchemaTests.Schema, "identity_telegram_link_changes")).Should().BeTrue();
    }

    [Fact]
    public async Task Changes_ShouldRoundTrip_AndHoldOnePerBotAndNumber()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDatabaseSettings(fixture.ConnectionString);
        services.AddUserAccounts();
        await using var provider = services.BuildServiceProvider();
        (await provider.ApplyMigrationSetsAsync("test")).ValueOrThrow();
        var change = new IdentityTelegramLinkChange
        {
            Id = Guid.CreateVersion7(At),
            BotUserName = "ocharo_bot",
            PhoneNumber = "+998901234567",
            ChatId = 2002,
            TelegramUserId = 2002,
            RequestedAt = At,
        };
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            context.TelegramLinkChanges.Add(change);
            await context.SaveChangesAsync();
        }

        await using (var read = provider.CreateAsyncScope())
            (await read.ServiceProvider.GetRequiredService<IdentityDbContext>().TelegramLinkChanges.SingleAsync()).Should().BeEquivalentTo(change);

        await using var duplicate = provider.CreateAsyncScope();
        var again = duplicate.ServiceProvider.GetRequiredService<IdentityDbContext>();
        again.TelegramLinkChanges.Add(new IdentityTelegramLinkChange { Id = Guid.CreateVersion7(At), BotUserName = "ocharo_bot", PhoneNumber = "+998901234567", ChatId = 3003, TelegramUserId = 3003, RequestedAt = At });
        var save = () => again.SaveChangesAsync();
        (await save.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }
}

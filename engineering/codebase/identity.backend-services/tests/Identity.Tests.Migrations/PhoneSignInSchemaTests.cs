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
/// <c>004-phone-sign-in</c> on its own: apply → rollback → apply over the first three, its rows round-tripping through
/// <see cref="IdentityDbContext"/>, and one link per bot and number.
/// </summary>
/// <param name="fixture">The shared container.</param>
[Collection(PostgresMigratorCollection.Name)]
public sealed class PhoneSignInSchemaTests(MigratorPostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset At = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public async Task InitializeAsync() => await fixture.ResetAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ApplyRollbackApply_ShouldRebuildThePhoneSignInTables_AndLeaveTheSpine()
    {
        await using var migrator = IdentitySchemaTests.CreateMigrator(fixture);
        var runner = migrator.RunnerFor(IdentityDatabaseOptions.MigrationSetName);
        (await migrator.ApplyAllAsync()).ValueOrThrow();
        (await runner.RollbackAsync(targetOrdinal: 4)).ValueOrThrow().Should().Equal("009-telegram-messages", "008-telegram-link-changes", "007-invite-sign-up", "006-invitations", "005-display-name");

        (await runner.RollbackAsync(targetOrdinal: 3)).ValueOrThrow().Should().Equal("004-phone-sign-in");
        foreach (var table in SchemaShape.PhoneSignInTables)
            (await migrator.HasTableAsync(IdentitySchemaTests.Schema, table)).Should().BeFalse($"the rollback drops {table}");
        foreach (var table in SchemaShape.SpineTables)
            (await migrator.HasTableAsync(IdentitySchemaTests.Schema, table)).Should().BeTrue($"003-spine keeps {table}");

        (await migrator.ApplyAllAsync()).ValueOrThrow().Single().Applied.Should().Equal("004-phone-sign-in", "005-display-name", "006-invitations", "007-invite-sign-up", "008-telegram-link-changes", "009-telegram-messages");
        foreach (var table in SchemaShape.PhoneSignInTables)
            (await migrator.HasTableAsync(IdentitySchemaTests.Schema, table)).Should().BeTrue($"the second apply recreates {table}");
    }

    [Fact]
    public async Task PhoneSignInTables_ShouldRoundTripTheirRows_AndHoldOneLinkPerBotAndNumber()
    {
        await using var provider = await CreateProviderAsync();
        var code = new IdentityOtpCode
        {
            Id = Guid.CreateVersion7(At),
            SubjectHash = new string('a', 64),
            Scope = "identity.phone-sign-in",
            CodeHash = new string('b', 64),
            OriginHash = new string('c', 64),
            Attempts = 1,
            CreatedAt = At,
            ExpiresAt = At.AddMinutes(5),
        };
        var link = new IdentityTelegramLink { Id = Guid.CreateVersion7(At), BotUserName = "ocharo_bot", PhoneNumber = "+998901234567", ChatId = 4242, TelegramUserId = 4242, LinkedAt = At };
        var request = new IdentityTelegramLinkRequest
        {
            Id = Guid.CreateVersion7(At),
            TokenHash = new string('d', 64),
            Purpose = "phone-sign-in",
            BotUserName = "ocharo_bot",
            PhoneNumber = "+998901234567",
            CreatedAt = At,
            ExpiresAt = At.AddMinutes(15),
        };
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            context.OtpCodes.Add(code);
            context.TelegramLinks.Add(link);
            context.TelegramLinkRequests.Add(request);
            await context.SaveChangesAsync();
        }

        await using (var read = provider.CreateAsyncScope())
        {
            var stored = read.ServiceProvider.GetRequiredService<IdentityDbContext>();
            (await stored.OtpCodes.SingleAsync()).Should().BeEquivalentTo(code);
            (await stored.TelegramLinks.SingleAsync()).Should().BeEquivalentTo(link);
            (await stored.TelegramLinkRequests.SingleAsync()).Should().BeEquivalentTo(request);
        }

        await using var duplicate = provider.CreateAsyncScope();
        var again = duplicate.ServiceProvider.GetRequiredService<IdentityDbContext>();
        again.TelegramLinks.Add(new IdentityTelegramLink { Id = Guid.CreateVersion7(At), BotUserName = "ocharo_bot", PhoneNumber = "+998901234567", ChatId = 7, TelegramUserId = 7, LinkedAt = At });
        var save = () => again.SaveChangesAsync();
        (await save.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    private async Task<ServiceProvider> CreateProviderAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDatabaseSettings(fixture.ConnectionString);
        services.AddUserAccounts();
        var provider = services.BuildServiceProvider();
        (await provider.ApplyMigrationSetsAsync("test")).ValueOrThrow();
        return provider;
    }
}

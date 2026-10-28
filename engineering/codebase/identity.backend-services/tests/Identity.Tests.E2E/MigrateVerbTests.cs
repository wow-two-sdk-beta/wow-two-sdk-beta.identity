using AwesomeAssertions;
using Identity.Api.Configurations;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations.Verbs;

namespace Identity.Tests.E2E;

/// <summary>
/// The image's <c>migrate</c> verbs (<c>dotnet Identity.Api.dll migrate &lt;verb&gt;</c>) over the services its
/// <c>Program</c> builds, on a fresh database each: the exits, reruns that change nothing, a write-free <c>check</c> and
/// the <c>migrate-result</c> line. The host never starts, as in the image's migrate step.
/// </summary>
public sealed class MigrateVerbTests : IAsyncLifetime
{
    /// <summary>Every migration the identity set carries, in apply order.</summary>
    private static readonly string[] Migrations = ["001-accounts", "002-oidc", "003-spine"];

    private string _connectionString = "";
    private WebApplication _app = null!;

    /// <summary>The fresh database's name, which a guarded verb's <c>--confirm</c> names.</summary>
    private string Database => new NpgsqlConnectionStringBuilder(_connectionString).Database!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _connectionString = await PostgresTestServer.CreateDatabaseAsync();

        // Program.cs's own path to the verbs: the configured builder, built, never started.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = Environments.Development,
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DatabaseSettings:ConnectionString"] = _connectionString,
            ["Identity:Server:Keys:Ephemeral"] = "true",
            ["Logging:LogLevel:Default"] = "Warning",
        });
        builder.Configure();
        _app = builder.Build();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await PostgresTestServer.DropDatabaseAsync(_connectionString);
    }

    [Fact]
    public async Task Check_ShouldWriteNothing_OnAFreshAndOnAMigratedDatabase()
    {
        var verbs = Verbs();

        var fresh = await verbs.AssertCheckWritesNothingAsync();
        fresh.Exit.Should().Be(1, "a fresh database reads every set behind");
        Identity(fresh.Result).State.Should().Be(MigrationLevelState.Behind);

        var migrate = await verbs.AssertExitAsync(0);
        Identity(migrate.Result).Applied.Select(label => label.Label).Should().Equal(Migrations);

        var current = await verbs.AssertCheckWritesNothingAsync();
        current.Exit.Should().Be(0, current.ToString());
        Identity(current.Result).State.Should().Be(MigrationLevelState.Current);
        Identity(current.Result).LevelAfter.Should().Be(Migrations.Length);
    }

    [Fact]
    public async Task EveryRunnableVerb_ShouldRerunWithoutAChange()
    {
        var verbs = Verbs();

        await verbs.AssertRerunChangesNothingAsync("apply", "--dry-run");
        await verbs.AssertRerunChangesNothingAsync("apply");
        await verbs.AssertRerunChangesNothingAsync("seed");
        await verbs.AssertRerunChangesNothingAsync();
        foreach (var read in new[] { "check", "plan", "locks", "sets" })
            await verbs.AssertRerunChangesNothingAsync(read);
    }

    [Fact]
    public async Task Rollback_ShouldRefuseWithoutTheDatabaseName_ThenRollBackOnceAndApplyAgain()
    {
        var verbs = Verbs();
        await verbs.AssertExitAsync(0, "apply");

        (await verbs.AssertExitAsync(1, "rollback", "--to", "2")).Result.Message.Should().Contain("--confirm");
        await verbs.AssertExitAsync(1, "rollback", "--to", "2", "--confirm", "another_database");
        Identity((await verbs.AssertCheckWritesNothingAsync()).Result).State.Should().Be(MigrationLevelState.Current, "a refused rollback runs no SQL");

        await verbs.AssertExitAsync(0, "rollback", "--to", "2", "--confirm", Database);
        await verbs.AssertRerunChangesNothingAsync("rollback", "--to", "2", "--confirm", Database);
        (await verbs.AssertExitAsync(1, "check")).Result.Sets.Should().ContainSingle().Which.State.Should().Be(MigrationLevelState.Behind);

        var reapplied = await verbs.AssertExitAsync(0, "apply");
        Identity(reapplied.Result).Applied.Select(label => label.Label).Should().Equal("003-spine");
    }

    [Fact]
    public async Task Sets_ShouldDescribeTheIdentitySet_OnTheResultLine()
    {
        var sets = await Verbs().AssertExitAsync(0, "sets", "--json");

        sets.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1].Should().StartWith(MigrateRunResult.ResultLinePrefix);
        sets.Result.Verb.Should().Be("sets");
        Identity(sets.Result).Schema.Should().Be("identity");
        sets.Output.Should().Contain("\"identity_users\"", "the set publishes its referenceable tables");
    }

    /// <summary>The verb harness over the built host's services, snapshotting the fresh database.</summary>
    private MigrateVerbHarness Verbs() => new(_app.Services, async ct =>
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    });

    /// <summary>The identity set's entry on a result line.</summary>
    private static MigrateSetResult Identity(MigrateRunResult result)
        => result.Sets.Should().ContainSingle(set => set.Name == IdentityDatabaseOptions.MigrationSetName).Subject;
}

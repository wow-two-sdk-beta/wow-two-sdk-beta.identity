using Microsoft.Extensions.Configuration;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Identity.Tests.E2E;

/// <summary>Boots the real API and its SQL migrations over a throwaway PostgreSQL database on the tier's container.</summary>
public sealed class IdentityApiFixture : IAsyncLifetime
{
    /// <summary>Holds the public host the test target allows, beside <c>localhost</c>, as a deployed target's <c>AllowedHosts</c> does.</summary>
    public const string PublicHost = "identity.example.test";

    private readonly string _connectionString = Task.Run(PostgresTestServer.CreateDatabaseAsync).GetAwaiter().GetResult();

    public IdentityApiFixture()
    {
        Host = new WebApiTestHost<Program>
        {
            ConfigureConfigurationHook = configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseSettings:ConnectionString"] = _connectionString,
                ["AllowedHosts"] = PublicHost + ";localhost",
                ["Logging:LogLevel:Default"] = "Warning",
            }),
        };
    }

    /// <summary>Gets the booted API host.</summary>
    public WebApiTestHost<Program> Host { get; }

    Task IAsyncLifetime.InitializeAsync()
    {
        // Building a client starts the host, which creates the database and applies the migrations.
        using var client = Host.CreateClient();
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Host.DisposeAsync();
        await PostgresTestServer.DropDatabaseAsync(_connectionString);
    }
}

/// <summary>Shares one <see cref="IdentityApiFixture"/> across the whole E2E run.</summary>
[CollectionDefinition(Name)]
public sealed class IdentityApiCollection : ICollectionFixture<IdentityApiFixture>
{
    /// <summary>The collection name every E2E class joins.</summary>
    public const string Name = "identity-api";
}

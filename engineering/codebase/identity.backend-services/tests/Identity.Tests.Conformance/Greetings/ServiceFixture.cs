using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Client;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Identity.Tests.Conformance.Greetings;

/// <summary>Forms 2 and 3: the client wired as a consumer wires it, against the service host.</summary>
/// <remarks>By default the host runs in process (WebApplicationFactory) over a throwaway SQLite file. With
/// <c>CONFORMANCE_BASE_URL</c> set, the client calls that running host or image instead, on the real clock.</remarks>
public sealed class ServiceFixture : IAsyncLifetime
{
    /// <summary>Holds the variable that points the suite at a running host or image.</summary>
    public const string BaseUrlVariable = "CONFORMANCE_BASE_URL";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"identity-conformance-host-{Guid.NewGuid():N}.db");
    private readonly Uri? runningHost = Environment.GetEnvironmentVariable(BaseUrlVariable) is { Length: > 0 } url ? new Uri(url) : null;
    private WebApiTestHost<Program>? host;

    /// <summary>Gets the clock the service stamps greetings with.</summary>
    public TimeProvider Clock => host?.Clock ?? TimeProvider.System;

    /// <summary>Gets the consumer's container.</summary>
    public ServiceProvider Services { get; private set; } = null!;

    Task IAsyncLifetime.InitializeAsync()
    {
        var services = new ServiceCollection().AddLogging();
        if (runningHost is not null)
        {
            services.AddIdentityClient(options => options.BaseAddress = runningHost);
        }
        else
        {
            host = new WebApiTestHost<Program>
            {
                ConfigureConfigurationHook = configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DatabaseSettings:ConnectionString"] = $"Data Source={databasePath}",
                    ["Logging:LogLevel:Default"] = "Warning",
                }),
            };
            var server = host.Server;
            services.AddIdentityClient(options => options.BaseAddress = server.BaseAddress)
                .ConfigurePrimaryHttpMessageHandler(server.CreateHandler);
        }

        Services = services.BuildServiceProvider(validateScopes: true);
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Services.DisposeAsync();
        if (host is null)
            return;

        await host.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { databasePath, databasePath + "-shm", databasePath + "-wal" })
            File.Delete(path);
    }
}

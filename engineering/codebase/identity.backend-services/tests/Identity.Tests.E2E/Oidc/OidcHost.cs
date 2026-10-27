using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Identity.Tests.E2E.Oidc;

/// <summary>
/// The real API over a throwaway SQLite file with one test client registered, an organization role set and the clock
/// at the real time, so tokens validate against the JWKS as a relying party would.
/// </summary>
public sealed class OidcHost : IAsyncDisposable
{
    /// <summary>The test client's id.</summary>
    public const string ClientId = "e2e-app";

    /// <summary>The test client's throwaway secret.</summary>
    public const string ClientSecret = "e2e-test-only-client-secret";

    /// <summary>Where the test client receives codes.</summary>
    public const string RedirectUri = "https://app.example.test/signin-oidc";

    /// <summary>Where end-session returns the test client.</summary>
    public const string PostLogoutRedirectUri = "https://app.example.test/signed-out";

    /// <summary>The audience of the test client's access tokens.</summary>
    public const string Audience = "e2e-api";

    /// <summary>The test client's permission namespace.</summary>
    public const string PermissionNamespace = "e2e";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"identity-oidc-{Guid.NewGuid():N}.db");

    /// <summary>Create the host, with <paramref name="settings"/> over the defaults (ephemeral keys from <c>appsettings.Development.json</c>).</summary>
    /// <param name="settings">Extra host settings, such as key files.</param>
    /// <param name="services">Extra service changes.</param>
    public OidcHost(IReadOnlyDictionary<string, string?>? settings = null, Action<IServiceCollection>? services = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["DatabaseSettings:ConnectionString"] = $"Data Source={_databasePath}",
            ["AllowedHosts"] = "localhost",
            ["Logging:LogLevel:Default"] = "Warning",
            ["Identity:Server:Clients:0:ClientId"] = ClientId,
            ["Identity:Server:Clients:0:ClientSecret"] = ClientSecret,
            ["Identity:Server:Clients:0:RedirectUris:0"] = RedirectUri,
            ["Identity:Server:Clients:0:PostLogoutRedirectUris:0"] = PostLogoutRedirectUri,
            ["Identity:Server:Clients:0:Audience"] = Audience,
            ["Identity:Server:Clients:0:PermissionNamespace"] = PermissionNamespace,
            ["Identity:Organizations:Roles:owner:0"] = "e2e:deploy",
            ["Identity:Organizations:Roles:owner:1"] = "logs:read",
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            values[key] = value;

        Host = new WebApiTestHost<Program>
        {
            ConfigureConfigurationHook = configuration => configuration.AddInMemoryCollection(values),
            ConfigureServicesHook = services,
        };
        Host.Clock.SetUtcNow(DateTimeOffset.UtcNow);
    }

    /// <summary>Gets the API host; its <see cref="WebApiTestHost{TEntryPoint}.Clock"/> is the server's clock.</summary>
    public WebApiTestHost<Program> Host { get; }

    /// <summary>A new browser-like client: HTTPS, cookies kept, redirects returned rather than followed.</summary>
    public OidcTestClient CreateClient() => new(Host);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();

        // Pooled connections hold the file open, so release them before the files go.
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _databasePath, _databasePath + "-shm", _databasePath + "-wal" })
            File.Delete(path);
    }
}

/// <summary>Shares one <see cref="OidcHost"/> across the code-flow tests.</summary>
public sealed class OidcHostFixture : IAsyncLifetime
{
    /// <summary>Gets the shared host.</summary>
    public OidcHost Server { get; } = new();

    Task IAsyncLifetime.InitializeAsync()
    {
        // Building a client starts the host: migrations, the key check and the client seeding.
        using var client = Server.Host.CreateClient();
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync() => await Server.DisposeAsync();
}

/// <summary>The code-flow tests' collection, apart from the shared API fixture.</summary>
[CollectionDefinition(Name)]
public sealed class OidcHostCollection : ICollectionFixture<OidcHostFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "identity-oidc";
}
